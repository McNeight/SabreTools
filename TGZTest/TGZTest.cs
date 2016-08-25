﻿using System;
using System.Collections.Generic;
using System.IO;
using SabreTools.Helper;

namespace SabreTools
{
	public class TGZTest
	{
		// User-defined variables
		private List<string> _inputs;
		private string _outdir;
		private string _tempdir;
		private bool _delete;
		private bool _romba;
		private ArchiveScanLevel _7z;
		private ArchiveScanLevel _gz;
		private ArchiveScanLevel _rar;
		private ArchiveScanLevel _zip;
		private Logger _logger;

		// We still need access permissions for each of the archive files as well, kind of like DATFromDir

		/// <summary>
		/// Create a new TGZTest object
		/// </summary>
		/// <param name="inputs">List of all inputted files and folders</param>
		/// <param name="outdir">Output directory (empty for default directory)</param>
		/// <param name="tempdir">Temporary directory for archive extraction</param>
		/// <param name="delete">True if input files should be deleted, false otherwise</param>
		/// <param name="romba">True if files should be output in Romba depot folders, false otherwise</param>
		/// <param name="sevenzip">Integer representing the archive handling level for 7z</param>
		/// <param name="gz">Integer representing the archive handling level for GZip</param>
		/// <param name="rar">Integer representing the archive handling level for RAR</param>
		/// <param name="zip">Integer representing the archive handling level for Zip</param>
		/// <param name="logger">Logger object for file and console output</param>
		public TGZTest(List<string> inputs, string outdir, string tempdir, bool delete,
			bool romba, int sevenzip, int gz, int rar, int zip, Logger logger)
		{
			_inputs = inputs;
			_outdir = (String.IsNullOrEmpty(outdir) ? "tgz" : outdir);
			_delete = delete;
			_romba = romba;
			_7z = (ArchiveScanLevel)(sevenzip < 0 || sevenzip > 2 ? 0 : sevenzip);
			_gz = (ArchiveScanLevel)(gz < 0 || gz > 2 ? 0 : gz);
			_rar = (ArchiveScanLevel)(rar < 0 || rar > 2 ? 0 : rar);
			_zip = (ArchiveScanLevel)(zip < 0 || zip > 2 ? 0 : zip);
			_logger = logger;
		}

		/// <summary>
		/// Main entry point for the program
		/// </summary>
		/// <param name="args">List of arguments to be parsed</param>
		public static void Main(string[] args)
		{
			// If output is being redirected, don't allow clear screens
			if (!Console.IsOutputRedirected)
			{
				Console.Clear();
			}

			// Perform initial setup and verification
			Logger logger = new Logger(true, "tgztest.log");
			logger.Start();

			// Credits take precidence over all
			if ((new List<string>(args)).Contains("--credits"))
			{
				Build.Credits();
				return;
			}

			// If there's no arguments, show help
			if (args.Length == 0)
			{
				Build.Help();
				logger.Close();
				return;
			}

			// Output the title
			Build.Start("TGZTest");

			// Set all default values
			bool help = false,
				delete = false,
				romba = false,
				tgz = true;
			int sevenzip = 0,
				gz = 2,
				rar = 2,
				zip = 0;
			string outdir = "",
				tempdir = "";
			List<string> inputs = new List<string>();

			// Determine which switches are enabled (with values if necessary)
			foreach (string arg in args)
			{
				string temparg = arg.Replace("\"", "").Replace("file://", "");

				switch (temparg)
				{
					case "-?":
					case "-h":
					case "--help":
						help = true;
						break;
					case "-d":
					case "--delete":
						delete = true;
						break;
					case "-r":
					case "--romba":
						romba = true;
						break;
					default:
						if (temparg.StartsWith("-7z=") || temparg.StartsWith("--7z="))
						{
							if (!Int32.TryParse(temparg.Split('=')[1], out sevenzip))
							{
								sevenzip = 0;
							}
						}
						else if (temparg.StartsWith("-gz=") || temparg.StartsWith("--gz="))
						{
							if (!Int32.TryParse(temparg.Split('=')[1], out gz))
							{
								gz = 2;
							}
						}
						else if (temparg.StartsWith("-out=") || temparg.StartsWith("--out="))
						{
							outdir = temparg.Split('=')[1];
						}
						else if (temparg.StartsWith("-rar=") || temparg.StartsWith("--rar="))
						{
							if (!Int32.TryParse(temparg.Split('=')[1], out rar))
							{
								rar = 2;
							}
						}
						else if (temparg.StartsWith("-t=") || temparg.StartsWith("--temp="))
						{
							tempdir = temparg.Split('=')[1];
						}
						else if (temparg.StartsWith("-zip=") || temparg.StartsWith("--zip="))
						{
							if (!Int32.TryParse(temparg.Split('=')[1], out zip))
							{
								zip = 0;
							}
						}
						else if (File.Exists(temparg) || Directory.Exists(temparg))
						{
							inputs.Add(temparg);
						}
						else
						{
							logger.Error("Invalid input detected: " + arg);
							Console.WriteLine();
							Build.Help();
							logger.Close();
							return;
						}
						break;
				}
			}

			// If help is set, show the help screen
			if (help)
			{
				Build.Help();
				logger.Close();
				return;
			}

			// If a switch that requires a filename is set and no file is, show the help screen
			if (inputs.Count == 0 && tgz)
			{
				logger.Error("This feature requires at least one input");
				Build.Help();
				logger.Close();
				return;
			}

			// If we are doing a simple sort
			if (tgz)
			{
				InitTGZTest(inputs, outdir, tempdir, delete, romba, sevenzip, gz, rar, zip, logger);
			}

			// If nothing is set, show the help
			else
			{
				Build.Help();
			}

			logger.Close();
			return;
		}

		/// <summary>
		/// Wrap sorting files using an input DAT
		/// </summary>
		/// <param name="inputs">List of all inputted files and folders</param>
		/// <param name="outdir">Output directory (empty for default directory)</param>
		/// <param name="tempdir">Temporary directory for archive extraction</param>
		/// <param name="delete">True if input files should be deleted, false otherwise</param>
		/// <param name="romba">True if files should be output in Romba depot folders, false otherwise</param>
		/// <param name="sevenzip">Integer representing the archive handling level for 7z</param>
		/// <param name="gz">Integer representing the archive handling level for GZip</param>
		/// <param name="rar">Integer representing the archive handling level for RAR</param>
		/// <param name="zip">Integer representing the archive handling level for Zip</param>
		/// <param name="logger">Logger object for file and console output</param>
		public static bool InitTGZTest(List<string> inputs, string outdir, string tempdir, bool delete,
			bool romba, int sevenzip, int gz, int rar, int zip, Logger logger)
		{
			// Get all individual files from the inputs
			List<string> newinputs = new List<string>();
			foreach (string input in inputs)
			{
				if (File.Exists(input))
				{
					newinputs.Add(Path.GetFullPath(input));
				}
				else if (Directory.Exists(input))
				{
					foreach (string file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
					{
						newinputs.Add(Path.GetFullPath(input));
					}
				}
			}

			TGZTest tgztest = new TGZTest(newinputs, outdir, tempdir, delete, romba, sevenzip, gz, rar, zip, logger);
			return tgztest.Process();
		}

		/// <summary>
		/// Process all input files
		/// </summary>
		/// <returns>True if processing was a success, false otherwise</returns>
		public bool Process()
		{
			foreach (string input in _inputs)
			{
				_logger.User("Processing file " + input);

				// Do an external scan of the file, if necessary
				ArchiveTools.WriteTorrentGZ(input, _outdir, _romba, _logger);

				// Process the file as an archive, if necessary

				// Delete the soruce file if we're supposed to
				if (_delete)
				{
					try
					{
						_logger.User("Attempting to delete " + input);
						File.Delete(input);
					}
					catch (Exception ex)
					{
						_logger.Error(ex.ToString());
					}
				}
			}

			return true;
		}
	}
}
