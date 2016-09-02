﻿using Mono.Data.Sqlite;
using SabreTools.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace SabreTools
{
	public partial class RombaSharp
	{
		#region Helper methods

		/// <summary>
		/// Initialize the Romba application from XML config
		/// </summary>
		private static void InitializeConfiguration()
		{
			// Get default values if they're not written
			int workers = 4,
				verbosity = 1,
				cores = 4,
				port = 4003;
			string logdir = "logs",
				tmpdir = "tmp",
				webdir = "web",
				baddir = "bad",
				dats = "dats",
				db = "db",
				connectionString = "";
			Dictionary<string, Tuple<long, bool>> depots = new Dictionary<string, Tuple<long, bool>>();

			// Get the XML text reader for the configuration file, if possible
			XmlTextReader xtr = DatTools.GetXmlTextReader(_config, _logger);

			/* XML file structure

			<romba>
				<general>
					<workers>4</workers>
					<logdir>logs</logdir>
					<tmpdir>tmp</tmpdir>
					<webdir>web</web>
					<baddir>bad</baddir>
					<verbosity>1</verbosity>
					<cores>4</cores>
				</general>
				<index>
					<dats>dats</dats>
					<db>db</db>
				</index>
				<depots>
					<depot>
						<root>depot</root>
						<maxsize>40000</maxsize>
						<online>true</online>
					</depot>
				</depots>
				<server>
					<port>4003</port>
				</server>
			</romba>

			*/

			// Now parse the XML file for settings
			if (xtr != null)
			{
				xtr.MoveToContent();
				while (!xtr.EOF)
				{
					// We only want elements
					if (xtr.NodeType != XmlNodeType.Element)
					{
						xtr.Read();
						continue;
					}

					switch (xtr.Name)
					{
						case "workers":
							workers = xtr.ReadElementContentAsInt();
							break;
						case "logdir":
							logdir = xtr.ReadElementContentAsString();
							break;
						case "tmpdir":
							tmpdir = xtr.ReadElementContentAsString();
							break;
						case "webdir":
							webdir = xtr.ReadElementContentAsString();
							break;
						case "baddir":
							baddir = xtr.ReadElementContentAsString();
							break;
						case "verbosity":
							verbosity = xtr.ReadElementContentAsInt();
							break;
						case "cores":
							cores = xtr.ReadElementContentAsInt();
							break;
						case "dats":
							dats = xtr.ReadElementContentAsString();
							break;
						case "db":
							db = xtr.ReadElementContentAsString();
							break;
						case "depot":
							XmlReader subreader = xtr.ReadSubtree();
							if (subreader != null)
							{
								string root = "";
								long maxsize = -1;
								bool online = true;

								while (!subreader.EOF)
								{
									// We only want elements
									if (subreader.NodeType != XmlNodeType.Element)
									{
										subreader.Read();
										continue;
									}

									switch (subreader.Name)
									{
										case "root":
											root = subreader.ReadElementContentAsString();
											break;
										case "maxsize":
											maxsize = subreader.ReadElementContentAsLong();
											break;
										case "online":
											online = subreader.ReadElementContentAsBoolean();
											break;
										default:
											subreader.Read();
											break;
									}
								}

								try
								{
									depots.Add(root, new Tuple<long, bool>(maxsize, online));
								}
								catch
								{
									// Ignore add errors
								}
							}

							xtr.Skip();
							break;
						case "port":
							port = xtr.ReadElementContentAsInt();
							break;
						default:
							xtr.Read();
							break;
					}
				}
			}

			// Now validate the values given
			if (workers < 1)
			{
				workers = 1;
			}
			if (workers > 8)
			{
				workers = 8;
			}
			if (!Directory.Exists(logdir))
			{
				Directory.CreateDirectory(logdir);
			}
			if (!Directory.Exists(tmpdir))
			{
				Directory.CreateDirectory(tmpdir);
			}
			if (!Directory.Exists(webdir))
			{
				Directory.CreateDirectory(webdir);
			}
			if (!Directory.Exists(baddir))
			{
				Directory.CreateDirectory(baddir);
			}
			if (verbosity < 0)
			{
				verbosity = 0;
			}
			if (verbosity > 3)
			{
				verbosity = 3;
			}
			if (cores < 1)
			{
				cores = 1;
			}
			if (cores > 16)
			{
				cores = 16;
			}
			if (!Directory.Exists(dats))
			{
				Directory.CreateDirectory(dats);
			}
			db = Path.GetFileNameWithoutExtension(db) + ".sqlite";
			connectionString = "Data Source=" + _db + ";Version = 3;";
			foreach (string key in depots.Keys)
			{
				if (!Directory.Exists(key))
				{
					Directory.CreateDirectory(key);
					File.CreateText(Path.Combine(key, ".romba_size"));
					File.CreateText(Path.Combine(key, ".romba_size.backup"));
				}
				else
				{
					if (!File.Exists(Path.Combine(key, ".romba_size")))
					{
						File.CreateText(Path.Combine(key, ".romba_size"));
					}
					if (!File.Exists(Path.Combine(key, ".romba_size.backup")))
					{
						File.CreateText(Path.Combine(key, ".romba_size.backup"));
					}
				}
			}
			if (port < 0)
			{
				port = 0;
			}
			if (port > 65535)
			{
				port = 65535;
			}

			// Finally set all of the fields
			_workers = workers;
			_logdir = logdir;
			_tmpdir = tmpdir;
			_webdir = webdir;
			_baddir = baddir;
			_verbosity = verbosity;
			_cores = cores;
			_dats = dats;
			_db = db;
			_connectionString = connectionString;
			_depots = depots;
			_port = port;
		}

		private static void DisplayDBStats()
		{
			_logger.User("This feature is not yet implemented: dbstats");
		}

		private static void DisplayMemoryStats()
		{
			_logger.User("This feature is not yet implemented: memstats");
		}

		/// <summary>
		/// Populate or refresh the database information
		/// </summary>
		/// <remarks>Each hash has the following attributes: size, crc, md5, sha-1, dathash, existss</remarks>
		private static void RefreshDatabase()
		{
			// Make sure the db is set
			if (String.IsNullOrEmpty(_db))
			{
				_db = "db.sqlite";
				_connectionString = "Data Source=" + _db + ";Version = 3;";
			}

			// Make sure the file exists
			if (!File.Exists(_db))
			{
				DBTools.EnsureDatabase(_dbSchema, _db, _connectionString);
			}

			// Make sure the dats dir is set
			if (String.IsNullOrEmpty(_dats))
			{
				_dats = "dats";
			}

			// Make sure the folder exists
			if (!Directory.Exists(_dats))
			{
				Directory.CreateDirectory(_dats);
			}

			// Create a List of dat hashes in the database (SHA-1)
			List<string> databaseDats = new List<string>();

			// Populate the List from the database
			string query = "SELECT UNIQUE value FROM data WHERE key=\"dat\"";
			using (SqliteConnection dbc = new SqliteConnection(_connectionString))
			{
				using (SqliteCommand slc = new SqliteCommand(query, dbc))
				{
					using (SqliteDataReader sldr = slc.ExecuteReader())
					{
						if (sldr.HasRows)
						{
							sldr.Read();
							string hash = sldr.GetString(0);
							if (!databaseDats.Contains(hash))
							{
								databaseDats.Add(hash);
							}
						}
					}
				}

				// Now create a Dictionary of dats to parse from what's not in the database (SHA-1, Path)
				Dictionary<string, string> toscan = new Dictionary<string, string>();

				// Loop through the datroot and add only needed files
				foreach (string file in Directory.EnumerateFiles(_dats, "*", SearchOption.AllDirectories))
				{
					Rom dat = FileTools.GetSingleFileInfo(file);

					// If the Dat isn't in the database and isn't already accounted for in the DatRoot, add it
					if (!databaseDats.Contains(dat.HashData.SHA1) && !toscan.ContainsKey(dat.HashData.SHA1))
					{
						toscan.Add(dat.HashData.SHA1, Path.GetFullPath(file));
					}

					// If the Dat is in the database already, remove it to find stragglers
					else if (databaseDats.Contains(dat.HashData.SHA1))
					{
						databaseDats.Remove(dat.HashData.SHA1);
					}
				}

				// Loop through the Dictionary and add all data
				foreach (string key in toscan.Keys)
				{
					// Parse the Dat if possible
					Dat tempdat = new Dat();
					tempdat = DatTools.Parse(toscan[key], 0, 0, tempdat, _logger);

					// If the Dat wasn't empty, add the information
					if (tempdat.Files.Count != 0)
					{
						// Loop through the parsed entries
						foreach (string romkey in tempdat.Files.Keys)
						{
							foreach (Rom rom in tempdat.Files[romkey])
							{
								query = "SELECT id FROM data WHERE key=\"size\" AND value=\"" + rom.HashData.Size + "\" AND ("
									+ "(key=\"crc\" AND (value=\"" + rom.HashData.CRC + "\" OR value=\"null\"))"
									+ "AND (key=\"md5\" AND value=\"" + rom.HashData.MD5 + "\" OR value=\"null\"))"
									+ "AND (key=\"sha1\" AND value=\"" + rom.HashData.SHA1 + "\" OR value=\"null\")))";
								using (SqliteCommand slc = new SqliteCommand(query, dbc))
								{
									using (SqliteDataReader sldr = slc.ExecuteReader())
									{
										// If the hash exists in the database, add the dat hash for that id
										if (sldr.HasRows)
										{
											sldr.Read();
											string id = sldr.GetString(0);

											string squery = "INSERT INTO data (id, key, value) VALUES (\"" + id + "\", \"dat\", \"" + key + "\")";
											using (SqliteCommand sslc = new SqliteCommand(squery, dbc))
											{
												sslc.ExecuteNonQuery();
											}
										}

										// If it doesn't exist, add the hash and the dat hash for a new id
										else
										{
											string squery = "INSERT INTO data (key, value) VALUES (\"size\", \"" + rom.HashData.Size + "\")";
											using (SqliteCommand sslc = new SqliteCommand(squery, dbc))
											{
												sslc.ExecuteNonQuery();
											}

											long id = -1;

											squery = "SELECT last_insertConstants.Rowid()";
											using (SqliteCommand sslc = new SqliteCommand(squery, dbc))
											{
												id = (long)sslc.ExecuteScalar();
											}

											squery = "INSERT INTO data (id, key, value) VALUES (\"" + id + "\", \"crc\", \"" + rom.HashData.CRC + "\"),"
												+ " (\"" + id + "\", \"md5\", \"" + rom.HashData.MD5 + "\"),"
												+ " (\"" + id + "\", \"sha1\", \"" + rom.HashData.SHA1 + "\"),"
												+ " (\"" + id + "\", \"dat\", \"" + key + "\"),"
												+ " (\"" + id + "\", \"exists\", \"false\")";
											using (SqliteCommand sslc = new SqliteCommand(squery, dbc))
											{
												sslc.ExecuteNonQuery();
											}
										}
									}
								}
							}
						}
					}
				}

				// Now loop through and remove all references to old Dats
				// TODO: Remove orphaned files as well
				foreach (string dathash in databaseDats)
				{
					query = "DELETE FROM data WHERE key=\"dat\" AND value=\"" + dathash + "\"";
					using (SqliteCommand slc = new SqliteCommand(query, dbc))
					{
						slc.ExecuteNonQuery();
					}
				}
			}
		}

		#endregion
	}
}