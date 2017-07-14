﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Cbr2Pdf.Properties;
using ImageProcessor;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SharpCompress.Readers;
// ReSharper disable AssignNullToNotNullAttribute

namespace Cbr2Pdf
{
    public partial class MainWindow : INotifyPropertyChanged
    {
        private IEnumerable<string> files = new List<string>();
        private string targetDirectory;
        private int filesDone;
        private int convertPercent;
        private readonly ConcurrentDictionary<string, string> failedFiles = new ConcurrentDictionary<string, string>();

        public int ConvertPercent { get { return this.convertPercent; } set { this.convertPercent = value; OnPropertyChanged(); } }
        public string TargetDirectory { get { return this.targetDirectory; } set { this.targetDirectory = value; OnPropertyChanged(); } }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadSettings();
        }

        private void bOpen_Click(object sender, RoutedEventArgs e)
        {
            using (var of = new FolderBrowserDialog())
            {
                of.ShowNewFolderButton = false;
                of.SelectedPath = Settings.Default.LastUsedDirectory;
                var result = of.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK) return;
                if (string.IsNullOrWhiteSpace(of.SelectedPath)) return;
                TargetDirectory = of.SelectedPath;
            }
        }

        private async void bConvert_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            this.filesDone = 0;
            this.failedFiles.Clear();
            UpdateProgress();
            this.files = Directory.EnumerateFiles(TargetDirectory, "*.cbr", SearchOption.TopDirectoryOnly);
            var tasks = new List<Task>();
            foreach (var file in this.files)
            {
                var t = ConvertFile(file);
                tasks.Add(t);
            }
            await Task.WhenAll(tasks);
        }

        private async Task ConvertFile(string file)
        {
            var tempDir = Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(file));
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            var result = await ExtractFile(file, tempDir);
            if (result)
                result = await CompressImages(file, tempDir);
            if (result)
                await CreatePdf(file, tempDir);
            Directory.Delete(tempDir, true);
            Interlocked.Increment(ref this.filesDone);
            UpdateProgress();
        }

        private async Task<bool> CreatePdf(string file, string tempDir)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var pdf = new PdfDocument();
                    foreach (var img in Directory.EnumerateFiles(tempDir).Where(x => x.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
                    {
                        var image = XImage.FromFile(img);
                        var page = pdf.AddPage();
                        page.Width = image.PixelWidth * 72 / image.HorizontalResolution;
                        page.Height = image.PixelHeight * 72 / image.HorizontalResolution;
                        var gfx = XGraphics.FromPdfPage(page);
                        gfx.DrawImage(image, 0, 0);
                    }
                    pdf.Save(Path.Combine(TargetDirectory, Path.GetFileNameWithoutExtension(file) + ".pdf"));
                    return true;
                }
                catch (Exception ex)
                {
                    this.failedFiles.TryAdd(file, ex.Message);
                    return false;
                }
            });
        }


        private async Task<bool> CompressImages(string file, string tempDir)
        {
            return await Task.Run(() =>
            {
                try
                {
                    foreach (var img in Directory.GetFiles(tempDir))
                    {
                        var bytes = File.ReadAllBytes(img);
                        using (var inStream = new MemoryStream(bytes))
                        {
                            using (var outStream = new MemoryStream())
                            {
                                using (var imageFactory = new ImageFactory(true))
                                {
                                    imageFactory.Load(inStream).Quality(40).Save(outStream);
                                }
                                outStream.Position = 0;
                                using (var fileStream = new FileStream(img, FileMode.Create))
                                {
                                    outStream.CopyTo(fileStream);
                                }
                            }
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    this.failedFiles.TryAdd(file, ex.Message);
                    return false;
                }
            });
        }

        private async Task<bool> ExtractFile(string file, string tempDir)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var reader = ReaderFactory.Open(stream);
                        while (reader.MoveToNextEntry())
                            if (!reader.Entry.IsDirectory)
                                reader.WriteEntryToDirectory(tempDir, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    this.failedFiles.TryAdd(file, ex.Message);
                    return false;
                }
            });
        }

        private void UpdateProgress()
        {
            ConvertPercent = (int)Math.Ceiling((double)this.filesDone / this.files.Count() * 100);
        }
    }

    public partial class MainWindow
    {
        private void SaveSettings()
        {
            Settings.Default.LastUsedDirectory = TargetDirectory;
            Settings.Default.Save();
        }

        private void LoadSettings()
        {
            TargetDirectory = Settings.Default.LastUsedDirectory;
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion INotifyPropertyChanged
    }
}