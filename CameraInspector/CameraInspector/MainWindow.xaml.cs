using AForge.Video.DirectShow;
using AForge.Video;
using Hompus.VideoInputDevices;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
using System.Threading;
using Window = System.Windows.Window;
using MaterialDesignColors;
using MaterialDesignThemes;
using MaterialDesignThemes.Wpf;
using System.Web.UI;
using System.Windows.Controls.Primitives;

namespace CameraInspector
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<FilterInfo> VideoDevices { get; set; }
        public FilterInfo CurrentDevice
        {
            get { return _currentDevice; }
            set { _currentDevice = value; this.OnPropertyChanged("CurrentDevice"); }
        }
        private FilterInfo _currentDevice;

        public System.Collections.Generic.IReadOnlyDictionary<int, string> VideoDevices2 { get; set; }
        public KeyValuePair<int, string> CurrentDevice2
        {
            get { return _currentDevice2; }
            set { _currentDevice2 = value; this.OnPropertyChanged("CurrentDevice2"); }
        }
        private KeyValuePair<int, string> _currentDevice2;

        public Boolean LightChecked { get; set; }
        public Boolean DarkChecked { get; set; }

        private IVideoSource _videoSourceAForge;

        VideoCapture capture;
        BackgroundWorker bkgWorker;

        MaterialDesignThemes.Wpf.Palette _palette;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            GetVideoDevices_AFORGE();
            GetVideoDevices_OpenCV();

            capture = new VideoCapture();
            GetTheme();


            LightChecked = true;
            DarkChecked = false;
            OnPropertyChanged(nameof(LightChecked));
            OnPropertyChanged(nameof(DarkChecked));

            this.Closing += MainWindow_Closing;                             
        }

        void GetTheme()
        {
            ResourceDictionary dictionary = Application.Current.Resources.MergedDictionaries.FirstOrDefault(x => x is IMaterialDesignThemeDictionary) ??
               Application.Current.Resources;
            ITheme theme = dictionary.GetTheme();

            if (dictionary != null)
            {
                theme.SetBaseTheme(LightChecked ? Theme.Light : Theme.Dark);
                dictionary.SetTheme(theme);
            }
        }

        private void btnAForgeStart_Click(object sender, RoutedEventArgs e)
        {
            StartCamera_AFORGE();
        }

        private void btnAForgeStop_Click(object sender, RoutedEventArgs e)
        {
            StopCamera_AFORGE();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCamera_AFORGE();
            StopCamera_OpenCV();
        }

        private void StartCamera_AFORGE()
        {
            if (CurrentDevice != null)
            {
                _videoSourceAForge = new VideoCaptureDevice(CurrentDevice.MonikerString);
                _videoSourceAForge.NewFrame += _videoSourceAForge_NewFrame;              
                _videoSourceAForge.Start();
            }
        }

        private void StartCamera_OpenCV()
        {
            StopCamera_OpenCV();
            Thread.Sleep(30);
            capture = new VideoCapture();
            capture.Open(CurrentDevice2.Key, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
            {
                capture = new VideoCapture();
                return;
            }

            bkgWorker = new BackgroundWorker { WorkerSupportsCancellation = true };
            bkgWorker.DoWork += Worker_DoWork;
            bkgWorker.RunWorkerAsync();
        }

        private void _videoSourceAForge_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                BitmapImage bi;
                using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    bi = bitmap.ToBitmapImage();
                }
                bi.Freeze(); // avoid cross thread operations and prevents leaks
                Dispatcher.BeginInvoke(new ThreadStart(delegate { AForgeVideoPlayer.Source = bi; }));
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error on _videoSource_NewFrame:\n" + exc.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopCamera_AFORGE();
            }
        }

        private void StopCamera_AFORGE()
        {
            if (_videoSourceAForge != null && _videoSourceAForge.IsRunning)
            {
                _videoSourceAForge.SignalToStop();
                _videoSourceAForge.NewFrame -= _videoSourceAForge_NewFrame;
                OpenCVVideoPlayer.Source = new BitmapImage();
            }
        }

        private void StopCamera_OpenCV()
        {
            try
            {
                if (bkgWorker != null)
                {
                    bkgWorker.CancelAsync();
                    bkgWorker.DoWork -= Worker_DoWork;
                    bkgWorker.Dispose();
                    OpenCVVideoPlayer.Source = new BitmapImage();
                }
                if (capture!=null && !capture.IsDisposed) capture.Dispose();
            }
            catch { }
        }

        private void GetVideoDevices_AFORGE()
        {
            VideoDevices = new ObservableCollection<FilterInfo>();
            foreach (FilterInfo filterInfo in new FilterInfoCollection(FilterCategory.VideoInputDevice))
            {
                VideoDevices.Add(filterInfo);
            }
            if (VideoDevices.Any())
            {
                CurrentDevice = VideoDevices[0];
            }
            else
            {
                MessageBox.Show("No video sources found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GetVideoDevices_OpenCV()
        {
            var sde = new SystemDeviceEnumerator();
            VideoDevices2 = sde.ListVideoInputDevice();
            this.OnPropertyChanged("VideoDevices2");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }

        private void btnOpenCVStart_Click(object sender, RoutedEventArgs e)
        {
            StartCamera_OpenCV();
        }

        private void btnOpenCStop_Click(object sender, RoutedEventArgs e)
        {
            StopCamera_OpenCV();
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var worker = (BackgroundWorker)sender;
            while (!worker.CancellationPending)
            {
                using (var frameMat = capture.RetrieveMat())
                {
                    //var rects = cascadeClassifier.DetectMultiScale(frameMat, 1.1, 5, HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(30, 30));

                    //foreach (var rect in rects)
                    //{
                    //    Cv2.Rectangle(frameMat, rect, Scalar.Red);
                    //}

                    // Must create and use WriteableBitmap in the same thread(UI Thread).
                    Dispatcher.Invoke(() =>
                    {
                        OpenCVVideoPlayer.Source = frameMat.ToWriteableBitmap();
                    });
                }

                Thread.Sleep(30);
            }
        }

        private void btnCamera_Click(object sender, RoutedEventArgs e)
        {
            //https://answers.opencv.org/question/41964/cv_cap_prop_settings-working-on-opencvsharp-not-on-opencv/
            //https://github.com/Kawaian/OpenCvSharp/blob/master/opencv/3.2/include/opencv2/videoio/videoio_c.h
            // CAP_PROP_SETTINGS Pop up video/camera filter dialog (note: only supported by DSHOW backend currently.
            // Windows only
            int CV_CAP_PROP_SETTINGS = 37;

            if (capture != null && !capture.IsDisposed && capture.IsOpened())
            {
                capture.Set(CV_CAP_PROP_SETTINGS, 0);
            }
        }

        private void RadioButton_Click(object sender, RoutedEventArgs e)
        {
            GetTheme();
        }
    }
}
