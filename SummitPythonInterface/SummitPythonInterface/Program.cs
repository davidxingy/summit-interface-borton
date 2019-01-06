﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Threading;
using System.Threading.Tasks;

// Summit API DLLs
using Medtronic.SummitAPI.Classes;
using Medtronic.SummitAPI.Events;
using Medtronic.TelemetryM;
using Medtronic.NeuroStim.Olympus.Commands;
using Medtronic.NeuroStim.Olympus.DataTypes.PowerManagement;
using Medtronic.NeuroStim.Olympus.DataTypes.Therapy;
using Medtronic.NeuroStim.Olympus.DataTypes.DeviceManagement;
using Medtronic.NeuroStim.Olympus.DataTypes.Sensing;

using NetMQ;
using NetMQ.Sockets;

using Newtonsoft.Json;

namespace SummitPythonInterface
{
    class Program
    {
        // Defining SummitSystem to be static so it can be properly accessed by sensing event handlers
        static SummitSystem theSummit;

        static void Main(string[] args)
        {
            // Tell user this code is not for human use
            Console.WriteLine("Starting Summit Stimulation Adjustment Training Project");
            Console.WriteLine("Before running this training project, the RLP should be used to configure a device to have two groups - A and B - with at least one program defined.");
            Console.WriteLine("This code is not for human use, either close program window or proceed by pressing a key");
            Console.ReadKey();
            Console.WriteLine("");

            // Initialize the Summit Interface
            Console.WriteLine("Creating Summit Interface...");

            // Create a manager
            SummitManager theSummitManager = new SummitManager("SummitTest");

            // Connect to the INS using a function based on the Summit Connect training code.
            theSummit = SummitConnect(theSummitManager);

            // Check if the connection attempt was successful
            if (theSummit == null)
            {
                Console.WriteLine("Failed to connect, press a key to close program.");
                Console.ReadKey();

                // Dispose SummitManager, disposing all SummitSystem objects
                theSummitManager.Dispose();
                return;
            }

            //check battery level and display
            BatteryStatusResult outputBuffer;
            APIReturnInfo commandInfo = theSummit.ReadBatteryLevel(out outputBuffer);

            TelemetryModuleInfo info;
            theSummit.ReadTelemetryModuleInfo(out info);

            Console.WriteLine();
            Console.WriteLine(String.Format("CTM Battery Level: {0}", info.BatteryLevel));

            // Ensure the command was successful before using the result
            if (commandInfo.RejectCode == 0)
            {
                string batteryLevel = outputBuffer.BatteryLevelPercent.ToString();
                Console.WriteLine("INS Battery Level: " + batteryLevel);
            }
            else
            {
                Console.WriteLine("Unable to read battery level");
            }
            Console.WriteLine();
            // Turn off sensing so we can config sensing
            theSummit.WriteSensingState(SenseStates.None, 0x00);

            // ******************* Create a sensing configuration for Time Domain channels *******************
            List<TimeDomainChannel> TimeDomainChannels = new List<TimeDomainChannel>(4);
            TdSampleRates the_sample_rate = TdSampleRates.Sample0500Hz;

            // First Channel Specific configuration: Channels 0 and 1 are Bore 0.
            // Sample rate must be consistent across all TD channels or disabled for individuals.
            // Channel differentially senses from contact 0 to contact 1
            // Evoked response mode disabled, standard operation
            // Low pass filter of 100Hz applied. 
            // Second low pass filter also at 100Hz applied
            // High pass filter at 8.6Hz applied.
            TimeDomainChannels.Add(new TimeDomainChannel(
                the_sample_rate,
                TdMuxInputs.Mux0,
                TdMuxInputs.Mux1,
                TdEvokedResponseEnable.Standard,
                TdLpfStage1.Lpf450Hz,
                TdLpfStage2.Lpf350Hz,
                TdHpfs.Hpf1_2Hz));

            // Second Channel Specific configuration: Channels 0 and 1 are Bore 0.
            // Sample rate must be consistent across all TD channels or disabled for individuals.
            // Channel differentially senses from contact 2 to contact 3
            // Evoked response mode disabled, standard operation
            // Low pass filter of 100Hz applied. 
            // Second low pass filter also at 100Hz applied
            // High pass filter at 8.6Hz applied.
            TimeDomainChannels.Add(new TimeDomainChannel(
                the_sample_rate,
                TdMuxInputs.Mux4,
                TdMuxInputs.Mux5,
                TdEvokedResponseEnable.Standard,
                TdLpfStage1.Lpf450Hz,
                TdLpfStage2.Lpf350Hz,
                TdHpfs.Hpf1_2Hz));

            // Third Channel Specific configuration: Channels 2 and 3 are Bore 1.
            // Sample rate must be consistent across all TD channels or disabled for individuals.
            // Channel differentially senses from contact 8 to contact 9 (Mux values indexed per bore)
            // Evoked response mode disabled, standard operation
            // Low pass filter of 100Hz applied. 
            // Second low pass filter also at 100Hz applied
            // High pass filter at 8.6Hz applied.
            TimeDomainChannels.Add(new TimeDomainChannel(
                the_sample_rate,
                TdMuxInputs.Mux0,
                TdMuxInputs.Mux1,
                TdEvokedResponseEnable.Standard,
                TdLpfStage1.Lpf450Hz,
                TdLpfStage2.Lpf350Hz,
                TdHpfs.Hpf1_2Hz));

            // Fourth Channel Specific configuration: Channels 2 and 3 are Bore 1.
            // Sample rate must be consistent across all TD channels or disabled for individuals.
            // Channel differentially senses from contact 10 to contact 11 (Mux values indexed per bore)
            // Evoked response mode disabled, standard operation
            // Low pass filter of 450Hz applied. 
            // Second low pass filter also at 350Hz applied
            // High pass filter at 1.2Hz applied.
            TimeDomainChannels.Add(new TimeDomainChannel(
                the_sample_rate,
                TdMuxInputs.Mux4,
                TdMuxInputs.Mux5,
                TdEvokedResponseEnable.Standard,
                TdLpfStage1.Lpf450Hz,
                TdLpfStage2.Lpf350Hz,
                TdHpfs.Hpf1_2Hz));

            // ******************* Set up the FFT *******************
            // Create a 256-element FFT that triggers every half second. Use a Hann window and stream all of the bins (if FFT streaming is enabled in later command).
            FftConfiguration fftChannel = new FftConfiguration();
            fftChannel.Size = FftSizes.Size0256;
            fftChannel.Interval = 500;
            fftChannel.WindowEnabled = true;
            fftChannel.WindowLoad = FftWindowAutoLoads.Hann100;
            fftChannel.StreamSizeBins = 0;
            fftChannel.StreamOffsetBins = 0;

            // ******************* Set up the Power channels *******************
            // Set up two power summation channels per time domain channel, use various bands for each.
            List<PowerChannel> powerChannels = new List<PowerChannel>();
            powerChannels.Add(new PowerChannel(10, 20, 30, 40));
            powerChannels.Add(new PowerChannel(11, 21, 31, 41));
            powerChannels.Add(new PowerChannel(12, 22, 32, 42));
            powerChannels.Add(new PowerChannel(13, 23, 33, 43));
            // Enable the calculation of the first band for every time domain channel.
            BandEnables theBandEnables = BandEnables.Ch0Band0Enabled | BandEnables.Ch1Band0Enabled | BandEnables.Ch2Band0Enabled | BandEnables.Ch3Band0Enabled;

            // ******************* Set up the miscellaneous settings *******************
            // Disable bridging functionality
            // Stream time domain data every 50ms.
            // Disable the loop recorder
            MiscellaneousSensing miscsettings = new MiscellaneousSensing();
            miscsettings.Bridging = BridgingConfig.None;
            miscsettings.StreamingRate = StreamingFrameRate.Frame30ms;
            miscsettings.LrTriggers = LoopRecordingTriggers.None;

            // ******************* Write the sensing configuration to the device *******************
            // Writing the sensing configuration must occur in a specific order.
            // Time domain channels must be configured before FFT, FFT must occur before power channels
            // If FFT or power channels are not being used they do not need to be configured
            // Miscellaneous settings need to be configured last (excluding accelerometer)
            // Accelerometer settings can be set at any time.
            Console.WriteLine("Writing sense configuration...");
            APIReturnInfo returnInfoBuffer;
            returnInfoBuffer = theSummit.WriteSensingTimeDomainChannels(TimeDomainChannels);
            Console.WriteLine("Write TD Config Status: " + returnInfoBuffer.Descriptor);
            returnInfoBuffer = theSummit.WriteSensingFftSettings(fftChannel);
            Console.WriteLine("Write FFT Config Status: " + returnInfoBuffer.Descriptor);
            returnInfoBuffer = theSummit.WriteSensingPowerChannels(theBandEnables, powerChannels);
            Console.WriteLine("Write Power Config Status: " + returnInfoBuffer.Descriptor);
            returnInfoBuffer = theSummit.WriteSensingMiscSettings(miscsettings);
            Console.WriteLine("Write Misc Config Status: " + returnInfoBuffer.Descriptor);
            returnInfoBuffer = theSummit.WriteSensingAccelSettings(AccelSampleRate.Sample64);
            Console.WriteLine("Write Accel Config Status: " + returnInfoBuffer.Descriptor);

            // ******************* Turn on LFP, FFT, and Power Sensing Components *******************
            returnInfoBuffer = theSummit.WriteSensingState(SenseStates.LfpSense | SenseStates.Fft | SenseStates.Power, 0x00);
            Console.WriteLine("Write Sensing Config Status: " + returnInfoBuffer.Descriptor);

            // ******************* Register the data listeners *******************
            theSummit.DataReceivedTDHandler += theSummit_DataReceived_TD;
            theSummit.DataReceivedPowerHandler += theSummit_DataReceived_Power;
            theSummit.DataReceivedFFTHandler += theSummit_DataReceived_FFT;
            theSummit.DataReceivedAccelHandler += theSummit_DataReceived_Accel;

            // ******************* Start streaming *******************
            // Start streaming for time domain, FFT, power, accelerometer, and time-synchronization.
            // Leave streaming of detector events, adaptive stim, and markers disabled
            returnInfoBuffer = theSummit.WriteSensingEnableStreams(true, false, false, false, false, true, true, false);
            Console.WriteLine("Write Stream Config Status: " + returnInfoBuffer.Descriptor);

            using (ResponseSocket stimSocket = new ResponseSocket())
            {
                stimSocket.Connect("tcp://192.168.4.2:12345");
                // Create some standard buffers for the output values form the various inc/dec functions. 
                APIReturnInfo bufferInfo = new APIReturnInfo();

                // TODO: allow group changes
                // int currentGroup = 0;
                double? currentFreq = 100;
                double?[] currentAmp = new double?[] {0,0,0,0};
                int?[] currentPW = new int?[] {250,250,250,250};

                // Turn off therapy, no ramp
                bufferInfo = theSummit.StimChangeTherapyOff(false);

                // Read the stimulation settings from the device
                TherapyGroup insStateGroupA;
                bufferInfo = theSummit.ReadStimGroup(GroupNumber.Group0, out insStateGroupA);

                // Write out device 0 and 1 slot 0 local and INS state
                Console.WriteLine("");

                Console.WriteLine("Group A Prog 0 INS State: Amp = " + insStateGroupA.Programs[0].AmplitudeInMilliamps.ToString()
                    + ", PW = " + insStateGroupA.Programs[0].PulseWidthInMicroseconds.ToString()
                    + ", Period = " + insStateGroupA.RatePeriod.ToString());

                Console.WriteLine("Group A Prog 1 INS State: Amp = " + insStateGroupA.Programs[1].AmplitudeInMilliamps.ToString()
                    + ", PW = " + insStateGroupA.Programs[1].PulseWidthInMicroseconds.ToString()
                    + ", Rate = " + insStateGroupA.RateInHz.ToString());

                // Change active group to 0
                bufferInfo = theSummit.StimChangeActiveGroup(ActiveGroup.Group0);
                Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);

                // Turn on therapy, if a POR reject is returned, attempt to reset it
                bufferInfo = theSummit.StimChangeTherapyOn();
                Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);

                // Reset POR if set
                if (bufferInfo.RejectCodeType == typeof(MasterRejectCode)
                    && (MasterRejectCode)bufferInfo.RejectCode == MasterRejectCode.ChangeTherapyPor)
                {
                    // Inform user
                    Console.WriteLine("POR set, resetting...");
                    // Reset POR
                    bufferInfo = resetPOR(theSummit);
                    bufferInfo = theSummit.StimChangeTherapyOn();
                }

                if (bufferInfo.RejectCode != 0)
                {
                    Console.WriteLine("Error during stim init, may not function properly. Error descriptor:" + bufferInfo.Descriptor);
                }
                Thread.Sleep(500);

                int waitPeriod = 5; // wait this much after each command is sent
                int bToothDelay = 160; // add this much wait to account for transmission delay
                bool verbose = false;
                try
                {
                    // Set amplitudes to 0
                    bufferInfo = theSummit.StimChangeStepAmp(0, -insStateGroupA.Programs[0].AmplitudeInMilliamps, out currentAmp[0]);
                    Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);
                    Thread.Sleep(waitPeriod);
                    bufferInfo = theSummit.StimChangeStepAmp(1, -insStateGroupA.Programs[1].AmplitudeInMilliamps, out currentAmp[1]);
                    Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);
                    Thread.Sleep(waitPeriod);

                    // Set pw's to 250
                    bufferInfo = theSummit.StimChangeStepPW(0, 250 - insStateGroupA.Programs[0].PulseWidthInMicroseconds, out currentPW[0]);
                    Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);
                    Thread.Sleep(waitPeriod);
                    bufferInfo = theSummit.StimChangeStepPW(1, 250 - insStateGroupA.Programs[1].PulseWidthInMicroseconds, out currentPW[1]);
                    Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);
                    Thread.Sleep(waitPeriod);
                    // Set the Stimulation Frequency to 100Hz, keep to sense friendly values
                    //bufferInfo = theSummit.StimChangeStepFrequency(100 - insStateGroupA.RateInHz, true, out currentFreq);
                    double freqDelta = 100 - insStateGroupA.RateInHz;
                    if (freqDelta != 0)
                    {
                        bufferInfo = theSummit.StimChangeStepFrequency(freqDelta, true, out currentFreq);
                        Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);
                        Thread.Sleep(waitPeriod);
                    }
                    Thread.Sleep(bToothDelay);

                    string gotMessage;
                    bool breakFlag = false;
                    while (!breakFlag)
                    {

                        //listening for messages is blocking for 1000 ms, after which it will check if it should exit thread, and if not, listen again (have this so that this thread isn't infinitely blocking when trying to join)
                        stimSocket.TryReceiveFrameString(TimeSpan.FromMilliseconds(1000), out gotMessage);
                        string ack;
                        if (gotMessage == null) //no actual message received, just the timeout being hit
                        {
                            Console.WriteLine(" Waiting for a message...");
                            continue;
                        }

                        StimParams stimParams = JsonConvert.DeserializeObject<StimParams>(gotMessage);

                        var updateOrder = new List<byte>() { 0, 0 };
                        if (stimParams.Amplitude[0] >= stimParams.Amplitude[1])
                        {
                            updateOrder[1] = 1;
                        }
                        else
                        {
                            updateOrder[0] = 1;
                        }
                        // Set the Stimulation Frequency, keep to sense friendly values
                        freqDelta = stimParams.Frequency - (double)currentFreq;
                        if (freqDelta != 0)
                        {
                            bufferInfo = theSummit.StimChangeStepFrequency(freqDelta, true, out currentFreq);
                            if (verbose)
                            { Console.WriteLine(" Command Status:" + bufferInfo.Descriptor); }
                            Thread.Sleep(waitPeriod);
                            if (bufferInfo.RejectCode != 0)
                            {
                                Console.WriteLine("Error during stim, may not function properly. Error descriptor:" + bufferInfo.Descriptor);
                                ack = "Exiting due to error";
                                stimSocket.SendFrame(ack);
                                breakFlag = true;
                            }
                        }
                        if (breakFlag) { break; }
                        int adjustedWait = stimParams.DurationInMilliseconds - (int)(3 * 1000 / currentFreq) - bToothDelay;
                        if (adjustedWait < 0) { adjustedWait = 10; }
                        Console.WriteLine("Adjusted wait time between trains is {0}", adjustedWait); 
                        foreach(byte i in updateOrder)
                        {
                            double deltaAmp = stimParams.Amplitude[i] - (double)currentAmp[i];
                                bufferInfo = theSummit.StimChangeStepAmp(i, deltaAmp, out currentAmp[i]);
                                if (verbose)
                                { Console.WriteLine(" Command Status:" + bufferInfo.Descriptor); }
                                Thread.Sleep(waitPeriod);
                                if (bufferInfo.RejectCode != 0)
                                {
                                    Console.WriteLine("Error during stim, may not function properly. Error descriptor:" + bufferInfo.Descriptor);
                                    ack = "Exiting due to error";
                                    stimSocket.SendFrame(ack);
                                    breakFlag = true;
                                }
                        }
                        if (breakFlag) { break; }

                        // Let it run for the requested duration (subtract effect of having to wait for 2 pulses)
                        Thread.Sleep(adjustedWait);
                        //Thread.Sleep(bToothDelay);

                        if (stimParams.AddReverse)
                        {
                            foreach (byte i in updateOrder)
                            {
                                double deltaAmp = stimParams.Amplitude[1 - i] - (double)currentAmp[i];
                                    bufferInfo = theSummit.StimChangeStepAmp(i, deltaAmp, out currentAmp[i]);
                                        if (verbose)
                                        { Console.WriteLine(" Command Status:" + bufferInfo.Descriptor); }
                                    Thread.Sleep(waitPeriod);
                                    if (bufferInfo.RejectCode != 0)
                                    {
                                        Console.WriteLine("Error during stim, may not function properly. Error descriptor:" + bufferInfo.Descriptor);
                                        ack = "Exiting due to error";
                                        stimSocket.SendFrame(ack);
                                        breakFlag = true;
                                    }
                            
                            }
                            if (breakFlag) { break; }
                            // Let the flipped condition run for the requested duration
                            Thread.Sleep(adjustedWait);
                            //Thread.Sleep(bToothDelay);
                        }

                        // Return amplitudes to zero
                        foreach (byte i in updateOrder)
                        {
                                bufferInfo = theSummit.StimChangeStepAmp(i, -(double)currentAmp[i], out currentAmp[i]);
                                if (verbose)
                                {
                                    Console.WriteLine(" Command Status:" + bufferInfo.Descriptor);
                                }
                                Thread.Sleep(waitPeriod);
                                if (bufferInfo.RejectCode != 0)
                                {
                                    Console.WriteLine("Error during stim, may not function properly. Error descriptor:" + bufferInfo.Descriptor);
                                    ack = "Exiting due to error";
                                    stimSocket.SendFrame(ack);
                                    breakFlag = true;
                                }
                         
                        }
                        if (breakFlag) { break; }

                        if (stimParams.ForceQuit)
                        {
                            ack = "Exited normally";
                            stimSocket.SendFrame(ack);
                            break;

                        }

                        ack = "Updated Stim";
                        stimSocket.SendFrame(ack);

                    }
                }
                catch (System.IO.IOException e)
                {
                    Console.WriteLine("Error : {0}", e.Message);
                }
                finally
                {
                    Console.WriteLine("");
                    Console.WriteLine("Shutting down stim...");
                    theSummit.StimChangeTherapyOff(false);

                    // ***** Object Disposal
                    Console.WriteLine("Stim stopped, press key to dispose Summit");
                    Console.ReadKey();

                    // Dispose SummitManager, disposing all SummitSystem objects
                    theSummitManager.Dispose();
                    Console.WriteLine("CLOSED");

                    // ***** Prompt user for final keypress before closing down the program.
                    Console.WriteLine("Press key to exit");
                    Console.ReadKey();
                }
            }
        }

        // Sensing data received event handlers
        private static void theSummit_DataReceived_TD(object sender, SensingEventTD TdSenseEvent)
        {
            // Announce to console that packet was received by handler
            //Console.WriteLine("TD Packet Received, Global SeqNum:" + TdSenseEvent.Header.GlobalSequence.ToString()
            //   + "; Time Generated:" + TdSenseEvent.GenerationTimeEstimate.Ticks.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());

            // Log some information about the received packet out to file

            theSummit.LogCustomEvent(TdSenseEvent.GenerationTimeEstimate, DateTime.Now, "TdPacketReceived", TdSenseEvent.Header.GlobalSequence.ToString());
        }

        private static void theSummit_DataReceived_FFT(object sender, SensingEventFFT FftSenseEvent)
        {
            // Announce to console that packet was received by handler
            //Console.WriteLine("FFT Packet Received, Global SeqNum:" + FftSenseEvent.Header.GlobalSequence.ToString()
            //    + "; Time Generated:" + FftSenseEvent.GenerationTimeEstimate.Ticks.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());

            // Log some information about the received packet out to file
            theSummit.LogCustomEvent(FftSenseEvent.GenerationTimeEstimate, DateTime.Now, "TdPacketReceived", FftSenseEvent.Header.GlobalSequence.ToString());
        }

        private static void theSummit_DataReceived_Power(object sender, SensingEventPower PowerSenseEvent)
        {
            // Announce to console that packet was received by handler
            //Console.WriteLine("Power Packet Received, Global SeqNum:" + PowerSenseEvent.Header.GlobalSequence.ToString()
            //    + "; Time Generated:" + PowerSenseEvent.GenerationTimeEstimate.Ticks.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());

            // Log some information about the received packet out to file
            theSummit.LogCustomEvent(PowerSenseEvent.GenerationTimeEstimate, DateTime.Now, "TdPacketReceived", PowerSenseEvent.Header.GlobalSequence.ToString());
        }

        private static void theSummit_DataReceived_Accel(object sender, SensingEventAccel AccelSenseEvent)
        {
            // Announce to console that packet was received by handler
            //Console.WriteLine("AccelPacket Received, Global SeqNum:" + AccelSenseEvent.Header.GlobalSequence.ToString()
            //    + "; Time Generated:" + AccelSenseEvent.GenerationTimeEstimate.Ticks.ToString() + "; Time Event Called:" + DateTime.Now.Ticks.ToString());

            // Log some information about the received packet out to file
            theSummit.LogCustomEvent(AccelSenseEvent.GenerationTimeEstimate, DateTime.Now, "TdPacketReceived", AccelSenseEvent.Header.GlobalSequence.ToString());
        }

        /// <summary>
        /// Resets the INS Power-On-Reset flag, which gets set when the device unexpectedly restarts. Can happen on low battery or on error. See logs for details. 
        /// </summary>
        /// <param name="theSummit">SummitSystem object to reset the POR on</param>
        /// <returns>APIReturn info object that details the POR flag reset results</returns>
        static APIReturnInfo resetPOR(SummitSystem theSummit)
        {
            Console.WriteLine("POR was set, resetting...");

            // reset POR
            theSummit.ResetErrorFlags(Medtronic.NeuroStim.Olympus.DataTypes.Core.StatusBits.Por);

            // check battery
            BatteryStatusResult theStatus;
            theSummit.ReadBatteryLevel(out theStatus);

            // perform interrogate command and check if therapy is enabled.s
            GeneralInterrogateData interrogateBuffer;
            APIReturnInfo theInfo = theSummit.ReadGeneralInfo(out interrogateBuffer);
            if (interrogateBuffer.IsTherapyUnavailable)
            {
                Console.WriteLine("Therapy still unavailable after reset");
            }

            // Return the info to main
            return theInfo;
        }

        /// <summary>
        /// Training function that illustrates a method of connecting to the Summit System
        /// </summary>
        /// <param name="projectName">ORCA defined project name</param>
        /// <returns></returns>
        private static SummitSystem SummitConnect(SummitManager theSummitManager)
        {
            // Bond with any CTMs plugged in over USB
            Console.WriteLine("Checking USB for unbonded CTMs. Please make sure they are powered on.");
            theSummitManager.GetUsbTelemetry();

            // Retrieve a list of known and bonded telemetry
            List<InstrumentInfo> knownTelemetry = theSummitManager.GetKnownTelemetry();

            // Check if any CTMs are currently bonded, poll the USB if not so that the user can be prompted to plug in a CTM over USB
            if (knownTelemetry.Count == 0)
            {
                do
                {
                    // Inform user we will loop until a CTM is found on USBs
                    Console.WriteLine("No bonded CTMs found, please plug a CTM in via USB...");
                    Thread.Sleep(2000);

                    // Bond with any CTMs plugged in over USB
                    knownTelemetry = theSummitManager.GetUsbTelemetry();
                } while (knownTelemetry.Count == 0);
            }

            // Write out the known instruments
            Console.WriteLine("Bonded Instruments Found:");
            foreach (InstrumentInfo inst in knownTelemetry)
            {
                Console.WriteLine(inst.SerialNumber);
            }

            // Connect to the first CTM available, then try others if it fails
            SummitSystem tempSummit = null; ;
            for (int i = 0; i < theSummitManager.GetKnownTelemetry().Count; i++)
            {
                // Perform the connection
                ManagerConnectStatus connectReturn = theSummitManager.CreateSummit(out tempSummit, theSummitManager.GetKnownTelemetry()[i]);

                // Write out the result
                Console.WriteLine("Create Summit Result: " + connectReturn.ToString());

                // Break if it failed successful
                if (connectReturn == ManagerConnectStatus.Success)
                {
                    break;
                }
            }

            // Make sure telemetry was connected to, if not fail
            if (tempSummit == null)
            {
                // inform user that CTM was not successfully connected to
                Console.WriteLine("Failed to connect to CTM...");
                return null;
            }
            else
            {
                // inform user that CTM was successfully connected to
                Console.WriteLine("CTM Connection Successful!");

                // Discovery INS with the connected CTM, loop until a device has been discovered
                List<DiscoveredDevice> discoveredDevices;
                do
                {
                    tempSummit.OlympusDiscovery(out discoveredDevices);
                } while (discoveredDevices.Count == 0);

                // Report Discovery Results to User
                Console.WriteLine("Olympi found:");
                foreach (DiscoveredDevice ins in discoveredDevices)
                {
                    Console.WriteLine(ins);
                }

                // Connect to the INS with default parameters and ORCA annotations
                Console.WriteLine("Creating Summit Interface.");

                // We can disable ORCA annotations because this is a non-human use INS (see disclaimer)
                // Human-use INS devices ignore the OlympusConnect disableAnnotation flag and always enable annotations.
                // Connect to a device
                ConnectReturn theWarnings;
                APIReturnInfo connectReturn;
                int i = 0;
                do
                {
                    connectReturn = tempSummit.StartInsSession(discoveredDevices[0], out theWarnings, true);
                    i++;
                } while (theWarnings.HasFlag(ConnectReturn.InitializationError));

                // Write out the number of times a StartInsSession was attempted with initialization errors
                Console.WriteLine("Initialization Error Count: " + i.ToString());

                // Write out the final result of the example
                if (connectReturn.RejectCode != 0)
                {
                    Console.WriteLine("Summit Initialization: INS failed to connect");
                    theSummitManager.DisposeSummit(tempSummit);
                    return null;
                }
                else
                {
                    // Write out the warnings if they exist
                    Console.WriteLine("Summit Initialization: INS connected, warnings: " + theWarnings.ToString());
                    return tempSummit;
                }
            }
        }
    }
}