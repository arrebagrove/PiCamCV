﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.UI;
using Kraken.Core;
using PiCamCV.Common;
using PiCamCV.WinForms.ExtensionMethods;

namespace PiCamCV.WinForms.UserControls
{
    public partial class FaceDetectionControl : CameraConsumerUserControl
    {
        private FileInfo haarEyeFile;
        private FileInfo haarFaceFile;
        private FaceDetector _faceDetector;
        private Rectangle _lastGoodSunnies;
        private AccessoryOverlay _sunglassOverlay2;
        private AccessoryOverlay _hatOverlay1;
        
        public FaceDetectionControl()
        {
            InitializeComponent();
        }
        public void ControlLoad(object sender, EventArgs e)
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var haarCascadePath = Path.Combine(new FileInfo(assemblyPath).DirectoryName, "haarcascades");
            haarEyeFile = new FileInfo(Path.Combine(haarCascadePath, "haarcascade_eye.xml"));
            haarFaceFile = new FileInfo(Path.Combine(haarCascadePath, "haarcascade_frontalface_default.xml"));

            _faceDetector = new FaceDetector(haarFaceFile.FullName, haarEyeFile.FullName);
            _lastGoodSunnies = Rectangle.Empty;

            _sunglassOverlay2 = new AccessoryOverlay(@"D:\Downloads\sunnies\sunglasses2.png");
            _hatOverlay1 = new AccessoryOverlay(@"D:\Downloads\sunnies\partyhat.png");
        }

        public override void ImageGrabbedHandler(object sender, EventArgs e)
        {
            using (var frame = new Mat())
            {
                CameraCapture.Retrieve(frame);
                
                var input = new FaceDetectorInput();
                input.Captured = frame;
                input.DetectEyes = chkDetectEyes.Checked;

                var result = _faceDetector.Process(input);
                var imageBgr = result.CapturedImage;
              //  IImage imageOut = imageBgr;

                if (chkRectangles.Checked)
                {
                    foreach (var face in result.Faces)
                    {
                        imageBgr.Draw(face.Region, new Bgr(Color.Red), 2);

                        var eyeCount = 0;
                        foreach (Rectangle eye in face.Eyes)
                        {
                            eyeCount++;
                            imageBgr.Draw(eye, new Bgr(Color.Blue), 2);
                            imageBgr.Draw(eyeCount.ToString(), eye.Location, FontFace.HersheyComplexSmall, 2, new Bgr(Color.Blue));
                        }
                    }
                }

                var inputBgra = imageBgr.Mat.ToImage<Bgra, byte>();
                Image<Bgra, byte> output = inputBgra;

                if (result.Faces.Any())
                {
                    if (chkSunnies.Checked)
                    {
                        output = WearSunnies2(output, result.Faces[0]);
                    }

                    if (chkHat.Checked)
                    {
                        output = WearHat(output, result.Faces[0]);
                    }
                }


                imageBox.Image = output;

                NotifyStatus("Face detection took {0}", result.Elapsed.ToHumanReadable());
            }
        }

        public Image<Bgra, byte> WearHat(Image<Bgra, byte> inputBgr, Face face)
        {
            _hatOverlay1.CalculatedRectangle = GetHatRectangle(face);
            return WearObject(inputBgr, _hatOverlay1);
        }

        private Rectangle GetHatRectangle(Face face)
        {
            var height = face.Region.Height * 3/4;
            var top = face.Region.Top - height;

            var width = face.Region.Width * 5 / 6;
            var left = (width/3) + face.Region.X;
            var hatRectangle = new Rectangle(left, top, width, height);
            return hatRectangle;
        }

        public Image<Bgra, byte> WearSunnies2(Image<Bgra, byte> inputBgr, Face face)
        {
            _sunglassOverlay2.CalculatedRectangle = GetSunglassRectangle(face);
            return WearObject(inputBgr, _sunglassOverlay2);
        }

        public Image<Bgra, byte> WearObject(Image<Bgra, byte> inputBgra, AccessoryOverlay accessory)
        {

            var bgraBlack = new Bgra(0, 0, 0, 0);

            if (accessory.FinalRectangle == Rectangle.Empty)
            {
                return inputBgra;
            }

            ConformRoi(accessory, inputBgra);

            var overlayRect = accessory.FinalRectangle;
            var resizeOverlayBgra = accessory.Overlay.Resize(overlayRect.Width, overlayRect.Height, Inter.Linear);

            var overlayTargetBgra = new Image<Bgra, byte>(inputBgra.Width, inputBgra.Height, bgraBlack);
            Log.Info(m => m("Overlay rect {0}", overlayRect));
            overlayTargetBgra.ROI = overlayRect;
            resizeOverlayBgra.CopyTo(overlayTargetBgra);
            overlayTargetBgra.ROI = Rectangle.Empty;

            const bool useMask = true;
            if (useMask)
            {
                var overlayMask = new Image<Gray, Byte>(inputBgra.Width, inputBgra.Height);
                CvInvoke.CvtColor(overlayTargetBgra, overlayMask, ColorConversion.Bgr2Gray);
                overlayMask = overlayMask.ThresholdBinary(new Gray(1), new Gray(1));
                inputBgra.SetValue(bgraBlack, overlayMask);
            }

            var outputBgra = inputBgra.AddWeighted(overlayTargetBgra, 1, 1, 1);

            inputBgra.ROI = Rectangle.Empty;
            outputBgra.ROI = Rectangle.Empty;

            return outputBgra;
        }

        /// <summary>
        /// If ROI goes beyond the image boundaries, things blow up
        /// </summary>
        private void ConformRoi(AccessoryOverlay accessory, Image<Bgra, byte> inputBgra)
        {
            var roiRect = accessory.CalculatedRectangle;
            
            var farRight = roiRect.X + roiRect.Width;
            if (farRight > inputBgra.Width)
            {
                roiRect.Width -= (farRight - inputBgra.Width);
            }

            if (roiRect.Y < 0)
            {
                roiRect.Height += roiRect.Y;
                roiRect.Y = 0;

            }
            accessory.CalculatedRectangle = roiRect;
        }

        private Rectangle GetSunglassRectangle(Face face)
        {
            var eyes = face.Eyes;
            var faceHeight = face.Region.Height;
            if (eyes.Count == 2)
            {
                eyes.Sort((e1, e2) => e1.Left.CompareTo(e2.Left));
                var leftEye = eyes[0];
                var rightEye = eyes[1];

                var eyeWidth = (rightEye.X - leftEye.X) + rightEye.Width;
                var eyeHeight = (int) (leftEye.Height*1.5);

                var widthFactor = (int) (eyeWidth*0.25);
                var heightFactor = (int)(faceHeight * 0.27);

                var width = eyeWidth + 2*widthFactor;
                var height = eyeHeight + heightFactor;

                var left = leftEye.X - widthFactor;
                var minEyeHeight = Math.Min(leftEye.Y, rightEye.Y);
                var maxEyeHeight = Math.Max(leftEye.Y, rightEye.Y);
                var top = (int) (minEyeHeight - (maxEyeHeight*0.1));

                var sunglassRect = new Rectangle(left, top, width, height);

                return sunglassRect;
            }
            else
            {
                return Rectangle.Empty;
            }
        }

        
    }
}

