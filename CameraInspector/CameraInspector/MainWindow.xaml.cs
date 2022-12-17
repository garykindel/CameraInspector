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
using Control = System.Windows.Controls.Control;
using System.Net.NetworkInformation;
using System.IO;
using LibUsbDotNet.Main;

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

        public PRODUCT_ID SelectedControlType { get; set; }

        public Boolean LightChecked { get; set; }
        public Boolean DarkChecked { get; set; }

        private IVideoSource _videoSourceAForge;

        VideoCapture capture;
        BackgroundWorker bkgWorker;

        internal System.Windows.Threading.DispatcherTimer drcTimer;
        tic m_tic;

        bool m_saveimage = false;
        bool m_connected = false;
        bool m_captureImages = false;
        bool m_movestage = false;
        bool m_processing = false;
        int m_stepmode = -1;
        int m_currentImageCount = 0;

        private int stepInterval = 3;
        private int stepDelay = 1000;
        private int stepLimit = 1000;
        private int stepSpeed = 5000000;
        private bool stepDirection = false;

        string ticStatus;
        string ticVariables;

        int ImageIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            GetVideoDevices_AFORGE();
            GetVideoDevices_OpenCV();

            capture = new VideoCapture();
           
            LightChecked = true;
            DarkChecked = false;
            OnPropertyChanged(nameof(LightChecked));
            OnPropertyChanged(nameof(DarkChecked));

            GetTheme();

            m_tic = new tic();

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

        void ConnectUSB()
        {
            try
            {
                if (!m_connected)
                {
                    drcTimer = new System.Windows.Threading.DispatcherTimer();
                    drcTimer.Tick += DrcTimer_Tick;

                    drcTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
                    drcTimer.Start();

                    m_tic = new tic();

                    if (m_stepmode == -1) SetStepMode(2);


                    tic.PRODUCT_ID? wControlType = (tic.PRODUCT_ID)Enum.Parse(typeof(tic.PRODUCT_ID), SelectedControlType.ToString(), true);
                    if (!wControlType.HasValue)
                    {
                        txtError.Text ="Controller not found";
                        return;
                    }
                    m_connected = m_tic.open((tic.PRODUCT_ID)wControlType);
                    m_tic.clear_driver_error();
                    m_tic.energize();
                    m_tic.exit_safe_start();

                    //m_tic.set_max_accel(100000);
                    //m_tic.set_max_decel(100000);
                    //m_tic.set_max_speed(50000000);
                    //m_tic.set_starting_speed(2000000);                   
                    //tic.wait_for_device_ready();

                    m_tic.halt_and_set_position(0);
                    m_tic.set_target_position(0);
                    m_tic.set_step_mode(m_stepmode);

                    if (m_connected)
                    {                        
                        txtTicStatus.Text = string.Format("Connected to {0}", SelectedControlType.ToString());
                        txtError.Text = "";
                    }
                }
                //RefreshTicInfo();
            }
            catch (Exception ex)
            {
                m_connected = false;
                txtError.Text = ex.Message;
            }
        }

        void DisconnectUSB()
        {
            try
            {
                if (drcTimer != null)
                {
                    drcTimer.Stop();
                    drcTimer.Tick -= DrcTimer_Tick;
                }
                m_tic.deenergize();
                m_tic.close();
                m_connected = false;
                if (!m_connected) txtTicStatus.Text = string.Format("Disconnected from {0}", SelectedControlType.ToString());
                txtActivity.Text = "";
            }
            catch (Exception ex)
            {
                txtError.Text = ex.Message;
            }
        }

        private void DrcTimer_Tick(object sender, EventArgs e)
        {
            // Current and target are not matching.
            // NEED TO REWORK

            // Initial state: m_captureImages true  m_processing false


            if (m_connected && m_tic != null)
            {
                if (m_captureImages)
                {
                    if (!string.IsNullOrEmpty(m_tic.get_error_status())) m_tic.clear_driver_error();

                    if (!m_processing)
                    {
                        m_currentImageCount++;
                        m_processing = true;
                        m_tic.set_target_position(m_tic.vars.current_position + Convert.ToInt32(UpSteps.Value));
                        m_tic.wait_for_move_complete();
                        if (capture != null && capture.IsOpened())
                        {
                            if (m_saveimage)
                            {
                                Saveshot();
                            }
                            else
                            {
                                //Takeshot(m_param);
                            }
                        }
                        //Task.Delay(StepDelay).Wait();
                        m_processing = false;
                    }
                    if (Math.Abs(m_tic.vars.current_position) >= UpStop.Value)
                    {
                        m_captureImages = false;
                    }
                    txtActivity.Text = string.Format("count: {0} current {1}: target: {2} Limit: {3} {4}", m_currentImageCount, m_tic.vars.current_position, m_tic.vars.target_position, UpStop.Value, System.DateTime.Now.ToString("HH:mm:ss:ffff"));
                }
                else
                {
                    //txtActivity.Text = string.Format("current {0}: target: {1} Limit: {2} {3}", m_tic.vars.current_position, m_tic.vars.target_position, MoveStageInternal, System.DateTime.Now.ToString("HH:mm:ss:ffff"));
                }
            }
            RefreshTicInfo();
        }

        void SetStepMode(object param)
        {
            try
            {
                if (param != null)
                {
                    m_stepmode = Convert.ToInt32(param);
                    if (m_tic != null && m_connected)
                    {
                        m_tic.set_step_mode(m_stepmode);
                    }
                }
            }
            catch (Exception ex)
            {
                txtError.Text = ex.Message;
            }
        }

        void RefreshTicInfo()
        {
            if (m_connected)
            {
                m_tic.reset_command_timeout();
                m_tic.get_variables(true);
                m_tic.get_status_variables(true);

                var sb = new StringBuilder();
                foreach (var prop in m_tic.status_vars.GetType().GetProperties())
                {

                    var wObject = prop.GetValue(m_tic.status_vars, null);
                    if (wObject != null)
                    {
                        sb.AppendLine(string.Format("{0}={1}", prop.Name, wObject));
                    }
                }
                ticStatus = sb.ToString();
                txtTicStatus.Text = string.Format("Op Status: {0} Err: {1} Step Mode: {2}  Model: {3}", m_tic.status_vars.operation_state, m_tic.status_vars.string_error_status, m_tic.vars.step_mode, SelectedControlType.ToString());

                sb = new StringBuilder();
                foreach (var prop in m_tic.vars.GetType().GetProperties())
                {
                    var wObject = prop.GetValue(m_tic.vars, null);
                    if (wObject != null)
                    {
                        sb.AppendLine(string.Format("{0}={1}", prop.Name, wObject));
                    }
                }
                ticVariables = sb.ToString();
            }

        }

        void Saveshot()
        {
            try
            {
                string imagefilePath = @"C:\Users\KindelG\Documents\AmScope\";
                ImageIndex++;
                if (capture != null && capture.IsOpened())
                {

                    var fileName = string.Format("{0}.jpg", $"{ImageIndex:0000}");
                    var fullpath = System.IO.Path.Combine(imagefilePath, fileName);

                    using (var fileStream = new FileStream(fullpath, FileMode.Create))
                    {
                        MemoryStream ms = new MemoryStream();
                        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                        encoder.QualityLevel = 100;
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(OpenCVVideoPlayer.Source as System.Windows.Media.Imaging.BitmapSource));
                        encoder.Save(ms);
                        fileStream.Position = 0;
                        ms.CopyTo(fileStream);
                        ms.WriteTo(fileStream);
                        ms.Flush();
                        fileStream.Flush();
                    }
                    //ImageHelper.CreateDRCEXIF(fullpath);

                    //OnPropertyChanged("Tabs");
                    //UIHelper.UpdateMemoryUsage();
                    txtActivity.Text = String.Format("Captured Image #{0}", $"{ImageIndex:0000}");
                }
            }
            catch (Exception ex)
            {
                txtError.Text = ex.Message;
                throw;
            }
        }

        void StartImageCapture()
        {
            try
            {
                if (capture != null && capture.IsOpened())
                {
                    m_currentImageCount = 0;
                    if (m_connected)
                    {
                        DisconnectUSB();
                        ConnectUSB();

                        //m_param = param;
                        m_tic.halt_and_hold();
                        m_tic.halt_and_set_position(0);
                        m_tic.set_target_position(0);
                        m_tic.set_max_speed((int)UpSpeed.Value);
                        m_captureImages = true;
                        m_processing = false;
                        m_saveimage = true;
                        ImageIndex = 0;
                    }
                    else
                    {
                        txtTicStatus.Text = "USB TIC controller not connected.";
                    }
                }
                else
                {
                    txtStatus.Text = "No camera connected.";
                }
            }
            catch (Exception ex)
            {
                txtError.Text = ex.Message;
            }
        }

        void StopImageCapture()
        {
            try
            {
                if (m_connected) m_tic.halt_and_hold();
                m_captureImages = false;
                //drcTimer.Stop();
                //drcTimer.Tick -= DrcTimer_Tick;
            }
            catch (Exception ex)
            {
                txtError.Text = ex.Message;
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
            if (m_connected) DisconnectUSB();
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
                this.AForgeVideoPlayer.Source = new BitmapImage();
                
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

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            ConnectUSB();
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            DisconnectUSB();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StartImageCapture();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopImageCapture();
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            this.grdMain.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        }

        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            this.grdMain.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Auto);
        }

        private void expOpenCVSharp4_Collapsed(object sender, RoutedEventArgs e)
        {
            this.grdMain.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Auto);
        }

        private void expOpenCVSharp4_Expanded(object sender, RoutedEventArgs e)
        {
            this.grdMain.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        }

        private void RadioButton_Click_1(object sender, RoutedEventArgs e)
        {
            SetStepMode((sender as RadioButton).Tag);
        }
    }
}
