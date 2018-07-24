using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.Devices.Spi;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Alcohol_MQ3_Tester
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>


    public sealed partial class MainPage : Page
    {
        private SpiDevice _mcp3008;

        public MainPage()
        {
            this.InitializeComponent();
        }

        public double GaugeValue
        {
            get { return this.mugPerLiterGauge.Value; }
            set
            {
                this.mugPerLiterGauge.Value = value;
                this.PromileTbx.Text = (value * 0.0023).ToString();
            }
        }


        public async Task GaugeTester()
        {
            //Two loops to test the gauge
            for (int ugL = 0; ugL <= 2000; ugL = ugL + 10)
            {
                Task wait = Task.Delay(1);
                GaugeValue = ugL;
                await wait;
            }
            for (int ugL = 2000; ugL >= 0; ugL = ugL - 10)
            {
                Task wait = Task.Delay(1);
                GaugeValue = ugL;
                await wait;
            }
        }

        private void MainGrid_Loaded(object sender, RoutedEventArgs e)
        {
            //Little funny thing: test the gauge
            GaugeTester().ConfigureAwait(false);
            PrepareSPI();
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            GaugeValue = 0;
            StartBtn.IsEnabled = true;
        }

        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            //read MQ3 sensor for 5 seconds and end with highest value
            Stopwatch stopWatch=new Stopwatch();
            stopWatch.Start();
            double highestValue = 0;
            StartBtn.IsEnabled = true;
            while (stopWatch.Elapsed < TimeSpan.FromMilliseconds(5000));
            {
                //From data sheet -- 1 byte selector for channel 0 on the ADC
                //First Byte sends the Start bit for SPI
                //Second Byte is the Configuration Bit
                //1 - single ended 
                //0 - d2
                //0 - d1
                //0 - d0
                //             S321XXXX <-- single-ended channel selection configure bits
                // Channel 0 = 10000000 = 0x80 OR (8+channel) << 4
                byte[] transmitBuffer = new byte[3] {1, 0x80, 0x00};


                byte[] receiveBuffer = new byte[3];

                _mcp3008.TransferFullDuplex(transmitBuffer, receiveBuffer);

                //first byte returned is 0 (00000000), 
                //second byte returned we are only interested in the last 2 bits 00000011 (mask of &3) 
                //then shift result 8 bits to make room for the data from the 3rd byte (makes 10 bits total)
                //third byte, need all bits, simply add it to the above result
                var result = ((receiveBuffer[1] & 3) << 8) + receiveBuffer[2];

                //Now a little math...
                //The max value is 10 mg/L and the least value is 0.05 mg/L what can be measured
                //We have 10 bits, so there are 1024 possible values
                //When we use ((Max value-Least value)/possible values) * result + least value      then we should get the right value
                //Least measured value: (9.95/1024.0) * 0 (Binairy 0000000000) + 0.1 = 0.1  mg/L
                //Max measured value: (9.95/1024) * 1024(Binairy 1111111111) + 0.1 = 10 mg/L

                double concentration = (9.95 / 1024.0) * result + 0.1;
                //now we have the result in mg/L but we need µg/L
                concentration = concentration * 1000;

                //check if concentration is higher than highestValue
                if (concentration > highestValue) highestValue = concentration;
            }
            //Update Gauge
            GaugeValue = highestValue;
            stopWatch.Stop();
        }

        private async void PrepareSPI()
        {
            //using SPI0 on the Pi
            var spiSettings = new SpiConnectionSettings(0);//for spi bus index 0
            spiSettings.ClockFrequency = 1000000; //1MHz
            spiSettings.Mode = SpiMode.Mode0;
            string spiQuery = SpiDevice.GetDeviceSelector("SPI0");

            //using Windows.Devices.Enumeration;
            var deviceInfo = await DeviceInformation.FindAllAsync(spiQuery);
            if (deviceInfo != null && deviceInfo.Count > 0)
            {
                _mcp3008 = await SpiDevice.FromIdAsync(deviceInfo[0].Id, spiSettings);
                StartBtn.IsEnabled = true;
            }
            else
            {
                ResourceLoader loader=new ResourceLoader();
                MessageDialog dialog=new MessageDialog(loader.GetString("Errormessage"), loader.GetString("ErrormessageHeader"));
                await dialog.ShowAsync();
                ResetBtn.IsEnabled = false;
            }
        }
    }
}
