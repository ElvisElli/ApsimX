﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MathNet.Numerics;
using Models;
using Models.Core;
using Models.Core.Run;
using Models.Interfaces;
using Newtonsoft.Json;
using static APSIM.Shared.Utilities.ProcessUtilities;

namespace Models.Climate
{
    /// <summary>
    /// A class which wraps the BestiaPop python tool to generate weather files on the fly
    /// for a given latitude/longitude/dates/etc.
    /// https://github.com/JJguri/bestiapop
    /// </summary>
    [ValidParent(ParentType = typeof(Simulation))]
    [ValidParent(ParentType = typeof(Zone))]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ViewName("UserInterface.Views.GridView")]
    [Serializable]
    public class BestiaPop : Model, IWeather, IReportsStatus
    {
        /// <summary>
        /// Summary file, used for logging purposes.
        /// </summary>
        [Link] private ISummary summary = null;

        /// <summary>
        /// Url of bestiapop github repo.
        /// </summary>
        private const string url = "https://github.com/JJguri/bestiapop";

        /// <summary>
        /// Path to which bestiapop is installed. It will be installed to this location
        /// if it does not already exist.
        /// </summary>
        private static readonly string bestiapopPath = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "ApsimInitiative",
                                    "ApsimX",
                                    "Python",
                                    "bestiapop");

        /// <summary>
        /// The weather file which has been generated by bestiapop.
        /// This is provides the bulk of the IWeather implementation.
        /// </summary>
        private IWeather weather;

        /// <summary>
        /// Output path (directory name). Leave blank to delete files after use.
        /// </summary>
        [Description("Output path")]
        [Tooltip("Leave blank to delete files after use")]
        [Display(Type = DisplayType.DirectoryName)]
        public string OutputPath { get; set; }

        /// <summary>
        /// Controls whether bestiapop is run in multi-process mode.
        /// </summary>
        [Description("Multi-process mode")]
        [Tooltip("Controls whether bestiapop is run in multi-process mode")]
        public bool MultiProcess { get; set; }

        /// <summary>
        /// Latitude.
        /// </summary>
        [Description("Latitude")]
        [Tooltip("Latitude")]
        public double Latitude { get; set; }

        /// <summary>
        /// Longitude.
        /// </summary>
        [Description("Longitude")]
        [Tooltip("Longitude")]
        public double Longitude { get; set; }

        /// <summary>
        /// Start date of the weather.
        /// </summary>
        [Description("Start date")]
        [Tooltip("Start date of the weather file")]
        public DateTime StartDate { get; set; }

        /// <summary>
        /// End date of the weather file.
        /// </summary>
        [Description("End date")]
        [Tooltip("End date of the weather file")]
        public DateTime EndDate { get; set; }

        // IWeather implementation.

        /// <summary>
        /// Maximum temperature.
        /// </summary>
        [Units("°C")]
        [JsonIgnore]
        public double MaxT
        {
            get => weather.MaxT;
            set => weather.MaxT = value;
        }

        /// <summary>
        /// Minimum temperature.
        /// </summary>
        [Units("°C")]
        [JsonIgnore]
        public double MinT
        {
            get => weather.MinT;
            set => weather.MinT = value;
        }

        /// <summary>
        /// Mean temperature.
        /// </summary>
        [Units("°C")]
        [JsonIgnore]
        public double MeanT => weather.MeanT;

        /// <summary>
        /// Mean VPD.
        /// </summary>
        [Units("hPa")]
        [JsonIgnore]
        public double VPD => weather.VPD;

        /// <summary>
        /// Rainfall.
        /// </summary>
        [Units("mm")]
        [JsonIgnore]
        public double Rain
        {
            get => weather.Rain;
            set => weather.Rain = value;
        }

        /// <summary>
        /// Solar radiation.
        /// </summary>
        [Units("MJ/m^2/d")]
        [JsonIgnore]
        public double Radn
        {
            get => weather.Radn;
            set => weather.Radn = value;
        }

        /// <summary>
        /// Vapor pressure.
        /// </summary>
        [Units("hPa")]
        [JsonIgnore]
        public double VP
        {
            get => weather.VP;
            set => weather.VP = value;
        }

        /// <summary>
        /// Wind value found in weather file or 3 if not found.
        /// </summary>
        /// <remarks>See <see cref="Weather.Wind"/>.</remarks>
        [JsonIgnore]
        public double Wind
        {
            get => weather.Wind;
            set => weather.Wind = value;
        }

        /// <summary>
        /// CO2 level. Default value if not found is 350.
        /// </summary>
        [JsonIgnore]
        public double CO2
        {
            get => weather.CO2;
            set => weather.CO2 = value;
        }

        /// <summary>
        /// Atmospheric pressure.
        /// </summary>
        [Units("hPa")]
        [JsonIgnore]
        public double AirPressure
        {
            get => weather.AirPressure;
            set => weather.AirPressure = value;
        }

        /// <summary>
        /// Average temperature.
        /// </summary>
        [Units("°C")]
        [JsonIgnore]
        public double Tav => weather.Tav;

        /// <summary>
        /// Temperature amplitude.
        /// </summary>
        [JsonIgnore]
        public double Amp => weather.Amp;

        /// <summary>
        /// Temperature amplitude.
        /// </summary>
        [JsonIgnore]
        public string FileName => weather.FileName;

        /// <summary>
        /// Tomorrow's met data.
        /// </summary>
        public DailyMetDataFromFile TomorrowsMetData => weather.TomorrowsMetData;

        /// <summary>
        /// Yesterday's met data.
        /// </summary>
        public DailyMetDataFromFile YesterdaysMetData => weather.YesterdaysMetData;

        /// <summary>Status message.</summary>
        public string Status { get; private set; }

        /// <summary>
        /// Called at start of simulation. Generates the weather file for
        /// use during the simulation.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        [EventSubscribe("StartOfSimulation")]
        private void OnStartOfSimulation(object sender, EventArgs args)
        {
            weather = GenerateWeatherFile();
        }

        /// <summary>
        /// Generate a weather file.
        /// </summary>
        /// <param name="cancelToken">Optional token which allows for cancellation of the child process.</param>
        public IWeather GenerateWeatherFile(CancellationToken cancelToken = default(CancellationToken))
        {
            if (!Directory.Exists(bestiapopPath))
            {
                Status = "Installing Bestiapop";
                InstallBestiapop(bestiapopPath);
            }

            string output = OutputPath;
            if (string.IsNullOrEmpty(output))
            {
                output = Path.Combine(Path.GetTempPath(), $"bestiapop-{Guid.NewGuid()}");
                if (summary != null)
                    summary.WriteMessage(this, $"OutputPath was not specified. Files will be generated to temp directory: '{output}'");
            }

            if (!Directory.Exists(output))
                Directory.CreateDirectory(output);

            string bestiapop = Path.Combine(bestiapopPath, "bestiapop", "bestiapop.py");
            StringBuilder args = new StringBuilder($"{bestiapop} -a generate-climate-file -s silo -y {StartDate.Year}-{EndDate.Year} -lat {Latitude} -lon {Longitude} ");

            // todo: check that these are correct variables
            args.Append("-c \"daily_rain max_temp min_temp vp vp_deficit evap_pan radiation et_short_crop\" ");
            if (MultiProcess)
                args.Append($"-m ");
            args.Append($"-o {output}");

            if (summary != null)
                summary.WriteMessage(this, $"Running bestiapop with command: 'python {args}' from directory {output}");

            Status = "Running bestiapop";
            try
            {
                string stdout = RunCommand("python", args.ToString(), output);
                summary.WriteMessage(this, $"Ran command 'python {args}' from directory '{output}'. Output from bestiapop:{Environment.NewLine}{stdout}");
            }
            catch (Exception err)
            {
                throw new Exception("Encountered an error while running bestiapop", err);
            }

            Weather result = CreateWeatherComponent(Directory.GetFiles(output, "*.met").FirstOrDefault());
            
            Status = null;

            return result;
        }

        /// <summary>
        /// Create a weather component for the specified file name and connect
        /// events/links if the simulation is already running.
        /// </summary>
        /// <param name="fileName">Path to the .met file.</param>
        private Weather CreateWeatherComponent(string fileName)
        {
            Weather result = new Weather();
            result.FullFileName = fileName;

            Simulation sim = FindAncestor<Simulation>();
            if (sim.IsRunning)
            {
                // Connect links.
                // We need to briefly hook the model up to the simulations tree for this to work.
                result.Parent = this;
                Links links = new Links(sim.Services);
                links.Resolve(result, true);
                result.Parent = null;

                // Connect events.
                Events events = new Events(result);
                events.ConnectEvents();

                object[] args = new object[] { this, EventArgs.Empty };
                events.Publish("Commencing", args);
                events.Publish("StartOfSimulation", args);
            }
            return result;
        }

        /// <summary>
        /// Install bestiapop at the given path.
        /// </summary>
        /// <param name="installPath">Path to which bestiapop is installed.</param>
        /// <remarks>
        /// This assumes that git and pip are installed and on path.
        /// </remarks>
        private static void InstallBestiapop(string installPath)
        {
            try
            {
                string parentDirectory = Directory.GetParent(installPath).FullName;
                if (!Directory.Exists(parentDirectory))
                    Directory.CreateDirectory(parentDirectory);
                CloneBestiapop(installPath);
                InstallDeps(installPath);
            }
            catch (Exception err)
            {
                try
                {
                    if (Directory.Exists(bestiapopPath))
                        Directory.Delete(bestiapopPath, true);
                }
                catch { /* Don't trap this error - we want to give the more informative error on what exactly went wrong. */ }

                throw new Exception("Unable to install bestiapop", err);
            }
        }

        /// <summary>
        /// Install required dependencies for bestiapop.
        /// </summary>
        /// <param name="path">Bestiapop install directory.</param>
        private static void InstallDeps(string path)
        {
            try
            {
                RunCommand("pip", "install -r requirements.txt", path);
            }
            catch (Exception err)
            {
                throw new Exception("Unable to install bestiapop requirements - is pip installed and on PATH?", err);
            }
        }

        /// <summary>
        /// Clone the bestiapop repo to a given directory.
        /// </summary>
        /// <param name="targetPath">The target directory. Bestiapop will be cloned into a subdirectory at this path.</param>
        private static void CloneBestiapop(string targetPath)
        {
            try
            {
                if (!Directory.Exists(targetPath))
                    RunCommand("git", $"clone {url} {targetPath}", Directory.GetParent(targetPath).FullName);
            }
            catch (Exception err)
            {
                throw new Exception("Unable to clone bestiapop", err);
            }
        }

        /// <summary>
        /// Gets the duration of the day in hours.
        /// </summary>
        /// <param name="Twilight">Angular distance between 90° and end of twilight - altitude of sun. +ve up, -ve down.</param>
        public double CalculateDayLength(double Twilight) => weather.CalculateDayLength(Twilight);

        /// <summary>
        /// Calculate time of sunrise in hours.
        /// </summary>
        public double CalculateSunRise() => weather.CalculateSunRise();

        /// <summary>
        /// Calculate time of sunset in hours.
        /// </summary>
        public double CalculateSunSet() => weather.CalculateSunSet();

        /// <summary>
        /// Run a shell command and return the output.
        /// Will throw if the command exits with non-0 exit code.
        /// </summary>
        /// <param name="fileName">Command to execute.</param>
        /// <param name="arguments">Command-line arguments to be passed.</param>
        /// <param name="workingDirectory">Working directory from which the command is to be run.</param>
        private static string RunCommand(string fileName, string arguments, string workingDirectory)
        {
            Process process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            StringBuilder output = new StringBuilder();
            process.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
            {
                output.AppendLine(e.Data);
            };
            process.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
            {
                output.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new Exception($"Error while running command '{fileName} {arguments}'. Process output: {output}");
            
            return output.ToString();
        }
    }
}
