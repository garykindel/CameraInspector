﻿using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using OpenCvSharp.WpfExtensions;
using ImageProcessor.Imaging;
using ImageProcessor;
using ResizeMode = ImageProcessor.Imaging.ResizeMode;

namespace CameraInspector
{
    //OPENCVSharp4
    //https://codereview.stackexchange.com/questions/245030/opencv-webcam-streaming-class-for-wpf
    //https://github.com/FrancescoBonizzi/WebcamControl-WPF-With-OpenCV

    public sealed class WebcamStreaming : IDisposable
    {
        private System.Drawing.Bitmap _lastFrame;
        private Task _previewTask;
        private VideoCapture _videoCapture;


        private CancellationTokenSource _cancellationTokenSource;

        private readonly Image _imageControlForRendering;
        private readonly int _frameWidth;
        private readonly int _frameHeight;

        public int CameraDeviceId { get; private set; }
        public byte[] LastPngFrame { get; private set; }
        public bool FlipHorizontally { get; set; }

        public VideoCapture VideoCapture { get => _videoCapture; set => _videoCapture = value; }

        public WebcamStreaming(
            Image imageControlForRendering,
            int cameraDeviceId)
        {
            _imageControlForRendering = imageControlForRendering;
            //_frameWidth = frameWidth;
            //_frameHeight = frameHeight;
            CameraDeviceId = cameraDeviceId;

        }

        public async Task Start()
        {
            // Never run two parallel tasks for the webcam streaming
            if (_previewTask != null && !_previewTask.IsCompleted)
                return;

            var initializationSemaphore = new SemaphoreSlim(0, 1);

            _cancellationTokenSource = new CancellationTokenSource();
            _previewTask = Task.Run(async () =>
            {
                try
                {
                    // Creation and disposal of this object should be done in the same thread 
                    // because if not it throws disconnectedContext exception
                    _videoCapture = new VideoCapture();

                    if (!_videoCapture.Open(CameraDeviceId))
                    {
                        throw new ApplicationException("Cannot connect to camera");
                    }

                    using (var frame = new Mat())
                    {
                        while (!_cancellationTokenSource.IsCancellationRequested)
                        {
                            _videoCapture.Read(frame);

                            if (!frame.Empty())
                            {

                                // Releases the lock on first not empty frame
                                if (initializationSemaphore != null)
                                    initializationSemaphore.Release();

                                //_lastFrame = FlipHorizontally
                                //    ? BitmapConverter.ToBitmap(frame.Flip(FlipMode.Y))
                                //    : BitmapConverter.ToBitmap(frame);

                                // (frame.ToBitmapSource() as WriteableBitmap).ToBitmapImage()

                                var lastFrameBitmapImage = frame.ToBitmapSource();
                                lastFrameBitmapImage.Freeze();
                                _imageControlForRendering.Dispatcher.Invoke(
                                    () => _imageControlForRendering.Source = lastFrameBitmapImage);
                            }

                            // 30 FPS
                            await Task.Delay(33);
                            GC.Collect();
                        }
                    }

                    _videoCapture?.Dispose();
                }
                finally
                {
                    if (initializationSemaphore != null)
                        initializationSemaphore.Release();
                }

            }, _cancellationTokenSource.Token);

            // Async initialization to have the possibility to show an animated loader without freezing the GUI
            // The alternative was the long polling. (while !variable) await Task.Delay
            await initializationSemaphore.WaitAsync();
            initializationSemaphore.Dispose();
            initializationSemaphore = null;

            if (_previewTask.IsFaulted)
            {
                // To let the exceptions exit
                await _previewTask;
            }
        }

        public async Task Stop()
        {
            // If "Dispose" gets called before Stop
            if (_cancellationTokenSource.IsCancellationRequested)
                return;

            if (!_previewTask.IsCompleted)
            {
                _cancellationTokenSource.Cancel();

                // Wait for it, to avoid conflicts with read/write of _lastFrame
                await _previewTask;
            }

            if (_lastFrame != null)
            {
                using (var imageFactory = new ImageFactory())
                using (var stream = new MemoryStream())
                {
                    imageFactory
                        .Load(_lastFrame)
                        .Resize(new ResizeLayer(
                            size: new System.Drawing.Size(_frameWidth, _frameHeight),
                            resizeMode: ResizeMode.Crop,
                            anchorPosition: AnchorPosition.Center))
                        .Save(stream);
                    LastPngFrame = stream.ToArray();
                }
            }
            else
            {
                LastPngFrame = null;
            }
        }

        public WriteableBitmap GetImage()
        {
            return _videoCapture.RetrieveMat().ToBitmapSource() as WriteableBitmap;
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _lastFrame?.Dispose();
            _videoCapture.Dispose();
            GC.Collect();
        }
    }
}
