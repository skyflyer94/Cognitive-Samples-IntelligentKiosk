﻿// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Cognitive Services: http://www.microsoft.com/cognitive
// 
// Microsoft Cognitive Services Github:
// https://github.com/Microsoft/Cognitive
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 

using IntelligentKioskSample.Controls;
using Microsoft.ProjectOxford.Common.Contract;
using Microsoft.ProjectOxford.Face.Contract;
using Newtonsoft.Json;

using ServiceHelpers;
using System;
using System.Diagnostics;
using System.Text;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Graphics.Imaging;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace IntelligentKioskSample.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    [KioskExperience(Title = "即時群眾人臉分析", ImagePath = "ms-appx:/Assets/realtime.png", ExperienceType = ExperienceType.Kiosk)]
    public sealed partial class RealTimeDemo : Page, IRealTimeDataProvider
    {
        private Task processingLoopTask;
        private bool isProcessingLoopInProgress;
        private bool isProcessingPhoto;

        private IEnumerable<Emotion> lastEmotionSample;
        private IEnumerable<Face> lastDetectedFaceSample;
        private IEnumerable<Tuple<Face, IdentifiedPerson>> lastIdentifiedPersonSample;
        private IEnumerable<SimilarFaceMatch> lastSimilarPersistedFaceSample;

        private DemographicsData demographics;
        private Dictionary<Guid, Visitor> visitors = new Dictionary<Guid, Visitor>();

        private int starving_count;
        private int last_latency;
        private int cur_latency;

        public RealTimeDemo()
        {
            this.InitializeComponent();
            this.DataContext = this;

            Window.Current.Activated += CurrentWindowActivationStateChanged;
            this.cameraControl.SetRealTimeDataProvider(this);
            this.cameraControl.FilterOutSmallFaces = true;
            //this.cameraControl.HideCameraControls();
            this.cameraControl.CameraAspectRatioChanged += CameraControl_CameraAspectRatioChanged;

            starving_count = 0;
            last_latency = -1;
            cur_latency = -2;
        }

        private void CameraControl_CameraAspectRatioChanged(object sender, EventArgs e)
        {
            this.UpdateCameraHostSize();
        }

        private void StartProcessingLoop()
        {
            this.isProcessingLoopInProgress = true;

            if (this.processingLoopTask == null || this.processingLoopTask.Status != TaskStatus.Running)
            {
                this.processingLoopTask = Task.Run(() => this.ProcessingLoop());
            }
        }


        private async void ProcessingLoop()
        {
            while (this.isProcessingLoopInProgress)
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    if (!this.isProcessingPhoto)
                    {
                        if (DateTime.Now.Hour != this.demographics.StartTime.Hour)
                        {
                            // We have been running through the hour. Reset the data...
                            await this.ResetDemographicsData();
                            this.UpdateDemographicsUI();
                        }

                        this.isProcessingPhoto = true;
                        last_latency = cur_latency;
                        this.starving_count = 0;
                        if (this.cameraControl.NumFacesOnLastFrame == 0)
                        {
                            await this.ProcessCameraCapture(null);
                        }
                        else
                        {
                            try
                            {
                                await this.ProcessCameraCapture(await this.cameraControl.CaptureFrameAsync());
                            }
                            catch(NullReferenceException e)
                            {
                                //ignore 
                                //await new MessageDialog("Error.", "Missing API Key").ShowAsync();
                                if (e.Source != null)
                                    this.debugText.Text = string.Format("NullRefenrenceException source: {0}", e.Source);
                            }
                        }
                    }
                    else
                    {
                        if (this.last_latency == this.cur_latency)
                        {
                            this.starving_count++;
                        }
                        if(this.starving_count > 7)
                        {
                            cur_latency = -1;
                            this.isProcessingPhoto = false;

                        }
                    }
                });

                await Task.Delay(2000);
            }
        }

        private async void CurrentWindowActivationStateChanged(object sender, Windows.UI.Core.WindowActivatedEventArgs e)
        {
            if ((e.WindowActivationState == Windows.UI.Core.CoreWindowActivationState.CodeActivated ||
                e.WindowActivationState == Windows.UI.Core.CoreWindowActivationState.PointerActivated) &&
                this.cameraControl.CameraStreamState == Windows.Media.Devices.CameraStreamState.Shutdown)
            {
                // When our Window loses focus due to user interaction Windows shuts it down, so we 
                // detect here when the window regains focus and trigger a restart of the camera.
                await this.cameraControl.StartStreamAsync(isForRealTimeProcessing: true);
            }
        }

        private async Task ProcessCameraCapture(ImageAnalyzer e)
        {
            if (e == null)
            {
                this.lastDetectedFaceSample = null;
                this.lastIdentifiedPersonSample = null;
                this.lastSimilarPersistedFaceSample = null;
                this.lastEmotionSample = null;
                this.debugText.Text = "";

                this.isProcessingPhoto = false;
                return;
            }

            DateTime start = DateTime.Now;

            // Compute Emotion, Age and Gender
            await Task.WhenAll(e.DetectEmotionAsync(), e.DetectFacesAsync(detectFaceAttributes: true));

            if (!e.DetectedEmotion.Any())
            {
                this.lastEmotionSample = null;
                this.ShowTimelineFeedbackForNoFaces();
            }
            else
            {
                this.lastEmotionSample = e.DetectedEmotion;

                EmotionScores averageScores = new EmotionScores
                {
                    Happiness = e.DetectedEmotion.Average(em => em.Scores.Happiness),
                    Anger = e.DetectedEmotion.Average(em => em.Scores.Anger),
                    Sadness = e.DetectedEmotion.Average(em => em.Scores.Sadness),
                    Contempt = e.DetectedEmotion.Average(em => em.Scores.Contempt),
                    Disgust = e.DetectedEmotion.Average(em => em.Scores.Disgust),
                    Neutral = e.DetectedEmotion.Average(em => em.Scores.Neutral),
                    Fear = e.DetectedEmotion.Average(em => em.Scores.Fear),
                    Surprise = e.DetectedEmotion.Average(em => em.Scores.Surprise)
                };

                this.emotionDataTimelineControl.DrawEmotionData(averageScores);
            }

            if (e.DetectedFaces == null || !e.DetectedFaces.Any())
            {
                this.lastDetectedFaceSample = null;
            }
            else
            {
                this.lastDetectedFaceSample = e.DetectedFaces;
            }

            // Compute Face Identification and Unique Face Ids
            await Task.WhenAll(e.IdentifyFacesAsync(), e.FindSimilarPersistedFacesAsync());

            if (!e.IdentifiedPersons.Any())
            {
                this.lastIdentifiedPersonSample = null;
            }
            else
            {
                this.lastIdentifiedPersonSample = e.DetectedFaces.Select(f => new Tuple<Face, IdentifiedPerson>(f, e.IdentifiedPersons.FirstOrDefault(p => p.FaceId == f.FaceId)));
            }

            if (!e.SimilarFaceMatches.Any())
            {
                this.lastSimilarPersistedFaceSample = null;
            }
            else
            {
                this.lastSimilarPersistedFaceSample = e.SimilarFaceMatches;
            }

            this.UpdateDemographics(e);

            this.debugText.Text = string.Format("Latency: {0}ms", (int)(DateTime.Now - start).TotalMilliseconds);
            this.cur_latency = (int)(DateTime.Now - start).TotalMilliseconds;
            this.isProcessingPhoto = false;
        }

        private void ShowTimelineFeedbackForNoFaces()
        {
            this.emotionDataTimelineControl.DrawEmotionData(new EmotionScores { Neutral = 1 });
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            EnterKioskMode();

            if (string.IsNullOrEmpty(SettingsHelper.Instance.EmotionApiKey) || string.IsNullOrEmpty(SettingsHelper.Instance.FaceApiKey))
            {
                await new MessageDialog("缺少臉部或情緒分析金鑰。請至設定頁面以完成輸入。", "缺乏金鑰").ShowAsync();
            }
            else
            {
                FaceListManager.FaceListsUserDataFilter = SettingsHelper.Instance.WorkspaceKey + "_RealTime";
                await FaceListManager.Initialize();

                await ResetDemographicsData();
                this.UpdateDemographicsUI();

                await this.cameraControl.StartStreamAsync(isForRealTimeProcessing: true);
                this.StartProcessingLoop();
            }

            base.OnNavigatedTo(e);
        }

        private void UpdateDemographics(ImageAnalyzer img)
        {
            if (this.lastSimilarPersistedFaceSample != null)
            {
                bool demographicsChanged = false;
                // Update the Visitor collection (either add new entry or update existing)
                int temp_count = 0;
                foreach (var item in this.lastSimilarPersistedFaceSample)
                {
                    Visitor visitor;
                    var CurTime = DateTime.Now;
                    if (this.visitors.TryGetValue(item.SimilarPersistedFace.PersistedFaceId, out visitor))
                    {
                        try
                        {
                            visitor.Date = CurTime.Date.ToString("yyyy/MM/dd");
                            visitor.Hour = CurTime.Hour;

                            if (this.lastIdentifiedPersonSample != null && this.lastIdentifiedPersonSample.Count() > temp_count && this.lastIdentifiedPersonSample.ElementAt(temp_count)!=null)
                            {
                                visitor.Name = this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2.Person.Name;
                            }
                            if (this.lastEmotionSample != null && this.lastEmotionSample.Count() > temp_count && this.lastEmotionSample.ElementAt(temp_count)!=null)
                            {
                                Emotion emo = this.lastEmotionSample.ElementAt(temp_count);
                                visitor.Smile = Math.Round(emo.Scores.Happiness, 4);
                            }
                            var messageString = JsonConvert.SerializeObject(visitor);
                            Task.Run(async () => { await AzureIoTHub.SendSQLToCloudMessageAsync(messageString); });
                        }
                        catch(NullReferenceException e)
                        {
                            this.debugText.Text = string.Format("NullRefenrenceException source at 1: {0}", e.Source);
                        }
                    }
                    else
                    {
                        try
                        {
                            demographicsChanged = true;
                            double smile;
                            if (this.lastEmotionSample != null && this.lastEmotionSample.Count() > temp_count && this.lastEmotionSample.ElementAt(temp_count)!=null)
                            {
                                Emotion emo = this.lastEmotionSample.ElementAt(temp_count);
                                smile = Math.Round(emo.Scores.Happiness, 4);
                            }
                            else
                            {
                                smile = 0;
                            }
                            int male = 1;
                            double age = item.Face.FaceAttributes.Age;
                            if (string.Compare(item.Face.FaceAttributes.Gender, "male", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                male = 0;
                            }
                            if (this.lastIdentifiedPersonSample != null && this.lastIdentifiedPersonSample.Count() > temp_count && this.lastIdentifiedPersonSample.ElementAt(temp_count)!=null)
                            {
                                string name = this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2.Person.Name;
                                if (name != null)
                                {
                                    visitor = new Visitor { UniqueId = item.SimilarPersistedFace.PersistedFaceId, Count = 1, Gender = male, Age = age, Smile = smile, Date = CurTime.Date.ToString("yyyy/MM/dd"), Hour = CurTime.Hour, Name = name };
                                }
                                else
                                {
                                    visitor = new Visitor { UniqueId = item.SimilarPersistedFace.PersistedFaceId, Count = 1, Gender = male, Age = age, Smile = smile, Date = CurTime.Date.ToString("yyyy/MM/dd"), Hour = CurTime.Hour };
                                }
                            }
                            else
                            {
                                visitor = new Visitor { UniqueId = item.SimilarPersistedFace.PersistedFaceId, Count = 1, Gender = male, Age = age, Smile = smile, Date = CurTime.Date.ToString("yyyy/MM/dd"), Hour = CurTime.Hour };
                            }
                            this.visitors.Add(visitor.UniqueId, visitor);
                            this.demographics.Visitors.Add(visitor);

                            // Update the demographics stats. We only do it for new visitors to avoid double counting.
                            var messageString = JsonConvert.SerializeObject(visitor);
                            Task.Run(async () => { await AzureIoTHub.SendSQLToCloudMessageAsync(messageString); });
                        }
                        catch (NullReferenceException e)
                        {
                            this.debugText.Text = string.Format("NullRefenrenceException source at 2: {0}", e.Source);
                        }

                    AgeDistribution genderBasedAgeDistribution = null;
                        if (string.Compare(item.Face.FaceAttributes.Gender, "male", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            this.demographics.OverallMaleCount++;
                            genderBasedAgeDistribution = this.demographics.AgeGenderDistribution.MaleDistribution;
                        }
                        else
                        {
                            this.demographics.OverallFemaleCount++;
                            genderBasedAgeDistribution = this.demographics.AgeGenderDistribution.FemaleDistribution;
                        }

                        if (item.Face.FaceAttributes.Age < 16)
                        {
                            genderBasedAgeDistribution.Age0To15++;
                        }
                        else if (item.Face.FaceAttributes.Age < 20)
                        {
                            genderBasedAgeDistribution.Age16To19++;
                        }
                        else if (item.Face.FaceAttributes.Age < 30)
                        {
                            genderBasedAgeDistribution.Age20s++;
                        }
                        else if (item.Face.FaceAttributes.Age < 40)
                        {
                            genderBasedAgeDistribution.Age30s++;
                        }
                        else if (item.Face.FaceAttributes.Age < 50)
                        {
                            genderBasedAgeDistribution.Age40s++;
                        }
                        else
                        {
                            genderBasedAgeDistribution.Age50sAndOlder++;
                        }
                    }

                    temp_count++;
                }

                if (demographicsChanged)
                {
                    this.ageGenderDistributionControl.UpdateData(this.demographics);
                }

                this.overallStatsControl.UpdateData(this.demographics);
            }
        }

        private void UpdateDemographicsUI()
        {
            this.ageGenderDistributionControl.UpdateData(this.demographics);
            this.overallStatsControl.UpdateData(this.demographics);
        }

        private async Task ResetDemographicsData()
        {
            this.initializingUI.Visibility = Visibility.Visible;
            this.initializingProgressRing.IsActive = true;

            this.demographics = new DemographicsData
            {
                StartTime = DateTime.Now,
                AgeGenderDistribution = new AgeGenderDistribution { FemaleDistribution = new AgeDistribution(), MaleDistribution = new AgeDistribution() },
                Visitors = new List<Visitor>()
            };

            this.visitors.Clear();
            await FaceListManager.ResetFaceLists();

            this.initializingUI.Visibility = Visibility.Collapsed;
            this.initializingProgressRing.IsActive = false;
        }

        public async Task HandleApplicationShutdownAsync()
        {
            await ResetDemographicsData();
        }

        private void EnterKioskMode()
        {
            ApplicationView view = ApplicationView.GetForCurrentView();
            if (!view.IsFullScreenMode)
            {
                view.TryEnterFullScreenMode();
            }
        }

        protected override async void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            this.isProcessingLoopInProgress = false;
            Window.Current.Activated -= CurrentWindowActivationStateChanged;
            this.cameraControl.CameraAspectRatioChanged -= CameraControl_CameraAspectRatioChanged;

            await this.ResetDemographicsData();

            await this.cameraControl.StopStreamAsync();
            base.OnNavigatingFrom(e);
        }

        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.UpdateCameraHostSize();
        }

        private void UpdateCameraHostSize()
        {
            this.cameraHostGrid.Width = this.cameraHostGrid.ActualHeight * (this.cameraControl.CameraAspectRatio != 0 ? this.cameraControl.CameraAspectRatio : 1.777777777777);
        }

        public EmotionScores GetLastEmotionForFace(BitmapBounds faceBox)
        {
            if (this.lastEmotionSample == null || !this.lastEmotionSample.Any())
            {
                return null;
            }

            return this.lastEmotionSample.OrderBy(f => Math.Abs(faceBox.X - f.FaceRectangle.Left) + Math.Abs(faceBox.Y - f.FaceRectangle.Top)).First().Scores;
        }

        public Face GetLastFaceAttributesForFace(BitmapBounds faceBox)
        {
            if (this.lastDetectedFaceSample == null || !this.lastDetectedFaceSample.Any())
            {
                return null;
            }

            return Util.FindFaceClosestToRegion(this.lastDetectedFaceSample, faceBox);
        }

        public IdentifiedPerson GetLastIdentifiedPersonForFace(BitmapBounds faceBox)
        {
            if (this.lastIdentifiedPersonSample == null || !this.lastIdentifiedPersonSample.Any())
            {
                return null;
            }

            Tuple<Face, IdentifiedPerson> match =
                this.lastIdentifiedPersonSample.Where(f => Util.AreFacesPotentiallyTheSame(faceBox, f.Item1.FaceRectangle))
                                               .OrderBy(f => Math.Abs(faceBox.X - f.Item1.FaceRectangle.Left) + Math.Abs(faceBox.Y - f.Item1.FaceRectangle.Top)).FirstOrDefault();
            if (match != null)
            {
                return match.Item2;
            }

            return null;
        }

        public SimilarPersistedFace GetLastSimilarPersistedFaceForFace(BitmapBounds faceBox)
        {
            if (this.lastSimilarPersistedFaceSample == null || !this.lastSimilarPersistedFaceSample.Any())
            {
                return null;
            }

            SimilarFaceMatch match =
                this.lastSimilarPersistedFaceSample.Where(f => Util.AreFacesPotentiallyTheSame(faceBox, f.Face.FaceRectangle))
                                               .OrderBy(f => Math.Abs(faceBox.X - f.Face.FaceRectangle.Left) + Math.Abs(faceBox.Y - f.Face.FaceRectangle.Top)).FirstOrDefault();

            return match?.SimilarPersistedFace;
        }

        private void emotionDataTimelineControl_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }

    [XmlType]
    public class Visitor
    {
        [XmlAttribute]
        public Guid UniqueId { get; set; }

        [XmlAttribute]
        public int Count { get; set; }

        [XmlAttribute]
        public int Gender { get; set; }

        [XmlAttribute]
        public double Age { get; set; }

        [XmlAttribute]
        public double Smile { get; set; }

        [XmlAttribute]
        public string Date { get; set; }

        [XmlAttribute]
        public int Hour { get; set; }

        [XmlAttribute]
        public string Name { get; set; }
    }

    [XmlType]
    public class AgeDistribution
    {
        public int Age0To15 { get; set; }
        public int Age16To19 { get; set; }
        public int Age20s { get; set; }
        public int Age30s { get; set; }
        public int Age40s { get; set; }
        public int Age50sAndOlder { get; set; }
    }

    [XmlType]
    public class AgeGenderDistribution
    {
        public AgeDistribution MaleDistribution { get; set; }
        public AgeDistribution FemaleDistribution { get; set; }
    }

    [XmlType]
    [XmlRoot]
    public class DemographicsData
    {
        public DateTime StartTime { get; set; }

        public AgeGenderDistribution AgeGenderDistribution { get; set; }

        public int OverallMaleCount { get; set; }

        public int OverallFemaleCount { get; set; }

        [XmlArrayItem]
        public List<Visitor> Visitors { get; set; }
    }
}