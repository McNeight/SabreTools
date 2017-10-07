﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

using SabreTools.Library.Data;
using SabreTools.Library.Items;

#if MONO
using System.IO;
#else
using Alphaleonis.Win32.Filesystem;
#endif

namespace SabreTools.Library.DatFiles
{
	public partial class DatFile
	{
		#region Instance Methods

		#region Bucketing

		/// <summary>
		/// Take the arbitrarily sorted Files Dictionary and convert to one sorted by a user-defined method
		/// </summary>
		/// <param name="bucketBy">SortedBy enum representing how to sort the individual items</param>
		/// <param name="deduperoms">Dedupe type that should be used</param>
		/// <param name="lower">True if the key should be lowercased (default), false otherwise</param>
		/// <param name="norename">True if games should only be compared on game and file name, false if system and source are counted</param>
		public void BucketBy(SortedBy bucketBy, DedupeType deduperoms, bool lower = true, bool norename = true)
		{
			// If we have a situation where there's no dictionary or no keys at all, we skip
			if (_items == null || _items.Count == 0)
			{
				return;
			}

			Globals.Logger.User("Organizing roms by {0}" + (deduperoms != DedupeType.None ? " and merging" : ""), bucketBy);

			// If the sorted type isn't the same, we want to sort the dictionary accordingly
			if (_sortedBy != bucketBy)
			{
				// Set the sorted type
				_sortedBy = bucketBy;

				// First do the initial sort of all of the roms inplace
				List<string> oldkeys = Keys.ToList();
				Parallel.ForEach(oldkeys, Globals.ParallelOptions, key =>
				{
					// Get the unsorted current list
					List<DatItem> roms = this[key];

					// Now add each of the roms to their respective games
					foreach (DatItem rom in roms)
					{
						// We want to get the key most appropriate for the given sorting type
						string newkey = GetKey(rom, bucketBy, lower, norename);

						// Add the DatItem to the dictionary
						Add(newkey, rom);
					}

					// Finally, remove the entire original key
					Remove(key);
				});
			}			

			// Now go through and sort all of the individual lists
			List<string> keys = Keys.ToList();
			Parallel.ForEach(keys, Globals.ParallelOptions, key =>
			{
				// Get the possibly unsorted list
				List<DatItem> sortedlist = this[key];

				// Sort the list of items to be consistent
				DatItem.Sort(ref sortedlist, false);

				// If we're merging the roms, do so
				if (deduperoms == DedupeType.Full || (deduperoms == DedupeType.Game && bucketBy == SortedBy.Game))
				{
					sortedlist = DatItem.Merge(sortedlist);
				}

				// Add the list back to the dictionary
				Remove(key);
				AddRange(key, sortedlist);
			});
		}

		/// <summary>
		/// Get the dictionary key that should be used for a given item and sorting type
		/// </summary>
		/// <param name="item">DatItem to get the key for</param>
		/// <param name="sortedBy">SortedBy enum representing what key to get</param>
		/// <param name="lower">True if the key should be lowercased (default), false otherwise</param>
		/// <param name="norename">True if games should only be compared on game and file name, false if system and source are counted</param>
		/// <returns>String representing the key to be used for the DatItem</returns>
		private string GetKey(DatItem item, SortedBy sortedBy, bool lower = true, bool norename = true)
		{
			// Set the output key as the default blank string
			string key = "";

			// Now determine what the key should be based on the sortedBy value
			switch (sortedBy)
			{
				case SortedBy.CRC:
					key = (item.Type == ItemType.Rom ? ((Rom)item).CRC : Constants.CRCZero);
					break;
				case SortedBy.Game:
					key = (norename ? ""
						: item.SystemID.ToString().PadLeft(10, '0')
							+ "-"
							+ item.SourceID.ToString().PadLeft(10, '0') + "-")
					+ (String.IsNullOrEmpty(item.MachineName)
							? "Default"
							: item.MachineName);
					if (lower)
					{
						key = key.ToLowerInvariant();
					}
					if (key == null)
					{
						key = "null";
					}

					key = HttpUtility.HtmlEncode(key);
					break;
				case SortedBy.MD5:
					key = (item.Type == ItemType.Rom
						? ((Rom)item).MD5
						: (item.Type == ItemType.Disk
							? ((Disk)item).MD5
							: Constants.MD5Zero));
					break;
				case SortedBy.SHA1:
					key = (item.Type == ItemType.Rom
						? ((Rom)item).SHA1
						: (item.Type == ItemType.Disk
							? ((Disk)item).SHA1
							: Constants.SHA1Zero));
					break;
				case SortedBy.SHA256:
					key = (item.Type == ItemType.Rom
						? ((Rom)item).SHA256
						: (item.Type == ItemType.Disk
							? ((Disk)item).SHA256
							: Constants.SHA256Zero));
					break;
				case SortedBy.SHA384:
					key = (item.Type == ItemType.Rom
						? ((Rom)item).SHA384
						: (item.Type == ItemType.Disk
							? ((Disk)item).SHA384
							: Constants.SHA384Zero));
					break;
				case SortedBy.SHA512:
					key = (item.Type == ItemType.Rom
						? ((Rom)item).SHA512
						: (item.Type == ItemType.Disk
							? ((Disk)item).SHA512
							: Constants.SHA512Zero));
					break;
			}

			// Double and triple check the key for corner cases
			if (key == null)
			{
				key = "";
			}

			return key;
		}

		#endregion

		#region Filtering

		/// <summary>
		/// Filter a DAT based on input parameters and modify the items
		/// </summary>
		/// <param name="filter">Filter object for passing to the DatItem level</param>
		/// <param name="trim">True if we are supposed to trim names to NTFS length, false otherwise</param>
		/// <param name="single">True if all games should be replaced by '!', false otherwise</param>
		/// <param name="root">String representing root directory to compare against for length calculation</param>
		public void Filter(Filter filter, bool single, bool trim, string root)
		{
			try
			{
				// Loop over every key in the dictionary
				List<string> keys = Keys.ToList();
				foreach (string key in keys)
				{
					// For every item in the current key
					List<DatItem> items = this[key];
					List<DatItem> newitems = new List<DatItem>();
					foreach (DatItem item in items)
					{
						// If the rom passes the filter, include it
						if (filter.ItemPasses(item))
						{
							// If we are in single game mode, rename all games
							if (single)
							{
								item.MachineName = "!";
							}

							// If we are in NTFS trim mode, trim the game name
							if (trim)
							{
								// Windows max name length is 260
								int usableLength = 260 - item.MachineName.Length - root.Length;
								if (item.Name.Length > usableLength)
								{
									string ext = Path.GetExtension(item.Name);
									item.Name = item.Name.Substring(0, usableLength - ext.Length);
									item.Name += ext;
								}
							}

							// Lock the list and add the item back
							lock (newitems)
							{
								newitems.Add(item);
							}
						}
					}

					Remove(key);
					AddRange(key, newitems);
				}
			}
			catch (Exception ex)
			{
				Globals.Logger.Error(ex.ToString());
			}
		}

		/// <summary>
		/// Use game descriptions as names in the DAT, updating cloneof/romof/sampleof
		/// </summary>
		public void MachineDescriptionToName()
		{
			try
			{
				// First we want to get a mapping for all games to description
				ConcurrentDictionary<string, string> mapping = new ConcurrentDictionary<string, string>();
				List<string> keys = Keys.ToList();
				Parallel.ForEach(keys, Globals.ParallelOptions, key =>
				{
					List<DatItem> items = this[key];
					foreach (DatItem item in items)
					{
						// If the key mapping doesn't exist, add it
						if (!mapping.ContainsKey(item.MachineName))
						{
							mapping.TryAdd(item.MachineName, item.Description.Replace('/', '_').Replace("\"", "''"));
						}
					}
				});

				// Now we loop through every item and update accordingly
				keys = Keys.ToList();
				Parallel.ForEach(keys, Globals.ParallelOptions, key =>
				{
					List<DatItem> items = this[key];
					List<DatItem> newItems = new List<DatItem>();
					foreach (DatItem item in items)
					{
						// Update machine name
						if (!String.IsNullOrEmpty(item.MachineName) && mapping.ContainsKey(item.MachineName))
						{
							item.MachineName = mapping[item.MachineName];
						}

						// Update cloneof
						if (!String.IsNullOrEmpty(item.CloneOf) && mapping.ContainsKey(item.CloneOf))
						{
							item.CloneOf = mapping[item.CloneOf];
						}

						// Update romof
						if (!String.IsNullOrEmpty(item.RomOf) && mapping.ContainsKey(item.RomOf))
						{
							item.RomOf = mapping[item.RomOf];
						}

						// Update sampleof
						if (!String.IsNullOrEmpty(item.SampleOf) && mapping.ContainsKey(item.SampleOf))
						{
							item.SampleOf = mapping[item.SampleOf];
						}

						// Add the new item to the output list
						newItems.Add(item);
					}

					// Replace the old list of roms with the new one
					Remove(key);
					AddRange(key, newItems);
				});
			}
			catch (Exception ex)
			{
				Globals.Logger.Warning(ex.ToString());
			}
		}

		/// <summary>
		/// Strip the given hash types from the DAT
		/// </summary>
		public void StripHashesFromItems()
		{
			// Output the logging statement
			Globals.Logger.User("Stripping requested hashes");

			// Now process all of the roms
			List<string> keys = Keys.ToList();
			Parallel.ForEach(keys, Globals.ParallelOptions, key =>
			{
				List<DatItem> items = this[key];
				for (int j = 0; j < items.Count; j++)
				{
					DatItem item = items[j];
					if (item.Type == ItemType.Rom)
					{
						Rom rom = (Rom)item;
						if ((StripHash & Hash.MD5) != 0)
						{
							rom.MD5 = null;
						}
						if ((StripHash & Hash.SHA1) != 0)
						{
							rom.SHA1 = null;
						}
						if ((StripHash & Hash.SHA256) != 0)
						{
							rom.SHA256 = null;
						}
						if ((StripHash & Hash.SHA384) != 0)
						{
							rom.SHA384 = null;
						}
						if ((StripHash & Hash.SHA512) != 0)
						{
							rom.SHA512 = null;
						}

						items[j] = rom;
					}
					else if (item.Type == ItemType.Disk)
					{
						Disk disk = (Disk)item;
						if ((StripHash & Hash.MD5) != 0)
						{
							disk.MD5 = null;
						}
						if ((StripHash & Hash.SHA1) != 0)
						{
							disk.SHA1 = null;
						}
						if ((StripHash & Hash.SHA256) != 0)
						{
							disk.SHA256 = null;
						}
						if ((StripHash & Hash.SHA384) != 0)
						{
							disk.SHA384 = null;
						}
						if ((StripHash & Hash.SHA512) != 0)
						{
							disk.SHA512 = null;
						}

						items[j] = disk;
					}
				}

				Remove(key);
				AddRange(key, items);
			});
		}

		#endregion

		#region Merging/Splitting Methods

		/// <summary>
		/// Use cdevice_ref tags to get full non-merged sets and remove parenting tags
		/// </summary>
		/// <param name="mergeroms">Dedupe type to be used</param>
		public void CreateDeviceNonMergedSets(DedupeType mergeroms)
		{
			Globals.Logger.User("Creating device non-merged sets from the DAT");

			// For sake of ease, the first thing we want to do is sort by game
			BucketBy(SortedBy.Game, mergeroms, norename: true);
			_sortedBy = SortedBy.Default;

			// Now we want to loop through all of the games and set the correct information
			AddRomsFromDevices();

			// Then, remove the romof and cloneof tags so it's not picked up by the manager
			RemoveTagsFromChild();

			// Finally, remove all sets that are labeled as bios or device
			//RemoveBiosAndDeviceSets(logger);
		}

		/// <summary>
		/// Use cloneof tags to create non-merged sets and remove the tags plus using the device_ref tags to get full sets
		/// </summary>
		/// <param name="mergeroms">Dedupe type to be used</param>
		public void CreateFullyNonMergedSets(DedupeType mergeroms)
		{
			Globals.Logger.User("Creating fully non-merged sets from the DAT");

			// For sake of ease, the first thing we want to do is sort by game
			BucketBy(SortedBy.Game, mergeroms, norename: true);
			_sortedBy = SortedBy.Default;

			// Now we want to loop through all of the games and set the correct information
			AddRomsFromDevices();
			AddRomsFromParent();

			// Now that we have looped through the cloneof tags, we loop through the romof tags
			AddRomsFromBios();

			// Then, remove the romof and cloneof tags so it's not picked up by the manager
			RemoveTagsFromChild();

			// Finally, remove all sets that are labeled as bios or device
			//RemoveBiosAndDeviceSets(logger);
		}

		/// <summary>
		/// Use cloneof tags to create merged sets and remove the tags
		/// </summary>
		/// <param name="mergeroms">Dedupe type to be used</param>
		public void CreateMergedSets(DedupeType mergeroms)
		{
			Globals.Logger.User("Creating merged sets from the DAT");

			// For sake of ease, the first thing we want to do is sort by game
			BucketBy(SortedBy.Game, mergeroms, norename: true);
			_sortedBy = SortedBy.Default;

			// Now we want to loop through all of the games and set the correct information
			AddRomsFromChildren();

			// Now that we have looped through the cloneof tags, we loop through the romof tags
			RemoveBiosRomsFromChild();

			// Finally, remove the romof and cloneof tags so it's not picked up by the manager
			RemoveTagsFromChild();
		}

		/// <summary>
		/// Use cloneof tags to create non-merged sets and remove the tags
		/// </summary>
		/// <param name="mergeroms">Dedupe type to be used</param>
		public void CreateNonMergedSets(DedupeType mergeroms)
		{
			Globals.Logger.User("Creating non-merged sets from the DAT");

			// For sake of ease, the first thing we want to do is sort by game
			BucketBy(SortedBy.Game, mergeroms, norename: true);
			_sortedBy = SortedBy.Default;

			// Now we want to loop through all of the games and set the correct information
			AddRomsFromParent();

			// Now that we have looped through the cloneof tags, we loop through the romof tags
			RemoveBiosRomsFromChild();

			// Finally, remove the romof and cloneof tags so it's not picked up by the manager
			RemoveTagsFromChild();
		}

		/// <summary>
		/// Use cloneof and romof tags to create split sets and remove the tags
		/// </summary>
		/// <param name="mergeroms">Dedupe type to be used</param>
		public void CreateSplitSets(DedupeType mergeroms)
		{
			Globals.Logger.User("Creating split sets from the DAT");

			// For sake of ease, the first thing we want to do is sort by game
			BucketBy(SortedBy.Game, mergeroms, norename: true);
			_sortedBy = SortedBy.Default;

			// Now we want to loop through all of the games and set the correct information
			RemoveRomsFromChild();

			// Now that we have looped through the cloneof tags, we loop through the romof tags
			RemoveBiosRomsFromChild();

			// Finally, remove the romof and cloneof tags so it's not picked up by the manager
			RemoveTagsFromChild();
		}

		#endregion

		#region Merging/Splitting Helper Methods

		/// <summary>
		/// Use romof tags to add roms to the children
		/// </summary>
		private void AddRomsFromBios()
		{
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				// If the game has no items in it, we want to continue
				if (this[game].Count == 0)
				{
					continue;
				}

				// Determine if the game has a parent or not
				string parent = null;
				if (!String.IsNullOrEmpty(this[game][0].RomOf))
				{
					parent = this[game][0].RomOf;
				}

				// If the parent doesnt exist, we want to continue
				if (String.IsNullOrEmpty(parent))
				{
					continue;
				}

				// If the parent doesn't have any items, we want to continue
				if (this[parent].Count == 0)
				{
					continue;
				}

				// If the parent exists and has items, we copy the items from the parent to the current game
				DatItem copyFrom = this[game][0];
				List<DatItem> parentItems = this[parent];
				foreach (DatItem item in parentItems)
				{
					// Figure out the type of the item and add it accordingly
					switch (item.Type)
					{
						case ItemType.Archive:
							Archive archive = ((Archive)item).Clone() as Archive;
							archive.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == archive.Name).Count() == 0 && !this[game].Contains(archive))
							{
								Add(game, archive);
							}

							break;
						case ItemType.BiosSet:
							BiosSet biosSet = ((BiosSet)item).Clone() as BiosSet;
							biosSet.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == biosSet.Name).Count() == 0 && !this[game].Contains(biosSet))
							{
								Add(game, biosSet);
							}

							break;
						case ItemType.Disk:
							Disk disk = ((Disk)item).Clone() as Disk;
							disk.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == disk.Name).Count() == 0 && !this[game].Contains(disk))
							{
								Add(game, disk);
							}

							break;
						case ItemType.Release:
							Release release = ((Release)item).Clone() as Release;
							release.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == release.Name).Count() == 0 && !this[game].Contains(release))
							{
								Add(game, release);
							}

							break;
						case ItemType.Rom:
							Rom rom = ((Rom)item).Clone() as Rom;
							rom.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == rom.Name).Count() == 0 && !this[game].Contains(rom))
							{
								Add(game, rom);
							}

							break;
						case ItemType.Sample:
							Sample sample = ((Sample)item).Clone() as Sample;
							sample.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == sample.Name).Count() == 0 && !this[game].Contains(sample))
							{
								Add(game, sample);
							}

							break;
					}
				}
			}
		}

		/// <summary>
		/// Use device_ref tags to add roms to the children
		/// </summary>
		private void AddRomsFromDevices()
		{
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				// If the game has no devices, we continue
				if (this[game][0].Devices == null || this[game][0].Devices.Count == 0)
				{
					continue;
				}

				// Determine if the game has any devices or not
				List<string> devices = this[game][0].Devices;
				foreach (string device in devices)
				{
					// If the device doesn't exist then we continue
					if (this[device].Count == 0)
					{
						continue;
					}

					// Otherwise, copy the items from the device to the current game
					DatItem copyFrom = this[game][0];
					List<DatItem> devItems = this[device];
					foreach (DatItem item in devItems)
					{
						// Figure out the type of the item and add it accordingly
						switch (item.Type)
						{
							case ItemType.Archive:
								Archive archive = ((Archive)item).Clone() as Archive;
								archive.CopyMachineInformation(copyFrom);
								if (this[game].Where(i => i.Name == archive.Name).Count() == 0 && !this[game].Contains(archive))
								{
									Add(game, archive);
								}

								break;
							case ItemType.BiosSet:
								BiosSet biosSet = ((BiosSet)item).Clone() as BiosSet;
								biosSet.CopyMachineInformation(copyFrom);
								if (this[game].Where(i => i.Name == biosSet.Name).Count() == 0 && !this[game].Contains(biosSet))
								{
									Add(game, biosSet);
								}

								break;
							case ItemType.Disk:
								Disk disk = ((Disk)item).Clone() as Disk;
								disk.CopyMachineInformation(copyFrom);
								if (this[game].Where(i => i.Name == disk.Name).Count() == 0 && !this[game].Contains(disk))
								{
									Add(game, disk);
								}

								break;
							case ItemType.Release:
								Release release = ((Release)item).Clone() as Release;
								release.CopyMachineInformation(copyFrom);
								if (this[game].Where(i => i.Name == release.Name).Count() == 0 && !this[game].Contains(release))
								{
									Add(game, release);
								}

								break;
							case ItemType.Rom:
								Rom rom = ((Rom)item).Clone() as Rom;
								rom.CopyMachineInformation(copyFrom);
								if (this[game].Where(i => i.Name == rom.Name).Count() == 0 && !this[game].Contains(rom))
								{
									Add(game, rom);
								}

								break;
							case ItemType.Sample:
								Sample sample = ((Sample)item).Clone() as Sample;
								sample.CopyMachineInformation(copyFrom);
								if (this[game].Where(i => i.Name == sample.Name).Count() == 0 && !this[game].Contains(sample))
								{
									Add(game, sample);
								}

								break;
						}
					}
				}
			}
		}

		/// <summary>
		/// Use cloneof tags to add roms to the children, setting the new romof tag in the process
		/// </summary>
		private void AddRomsFromParent()
		{
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				// If the game has no items in it, we want to continue
				if (this[game].Count == 0)
				{
					continue;
				}

				// Determine if the game has a parent or not
				string parent = null;
				if (!String.IsNullOrEmpty(this[game][0].CloneOf))
				{
					parent = this[game][0].CloneOf;
				}

				// If the parent doesnt exist, we want to continue
				if (String.IsNullOrEmpty(parent))
				{
					continue;
				}

				// If the parent doesn't have any items, we want to continue
				if (this[parent].Count == 0)
				{
					continue;
				}

				// If the parent exists and has items, we copy the items from the parent to the current game
				DatItem copyFrom = this[game][0];
				List<DatItem> parentItems = this[parent];
				foreach (DatItem item in parentItems)
				{
					// Figure out the type of the item and add it accordingly
					switch (item.Type)
					{
						case ItemType.Archive:
							Archive archive = ((Archive)item).Clone() as Archive;
							archive.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == archive.Name).Count() == 0 && !this[game].Contains(archive))
							{
								Add(game, archive);
							}

							break;
						case ItemType.BiosSet:
							BiosSet biosSet = ((BiosSet)item).Clone() as BiosSet;
							biosSet.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == biosSet.Name).Count() == 0 && !this[game].Contains(biosSet))
							{
								Add(game, biosSet);
							}

							break;
						case ItemType.Disk:
							Disk disk = ((Disk)item).Clone() as Disk;
							disk.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == disk.Name).Count() == 0 && !this[game].Contains(disk))
							{
								Add(game, disk);
							}

							break;
						case ItemType.Release:
							Release release = ((Release)item).Clone() as Release;
							release.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == release.Name).Count() == 0 && !this[game].Contains(release))
							{
								Add(game, release);
							}

							break;
						case ItemType.Rom:
							Rom rom = ((Rom)item).Clone() as Rom;
							rom.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == rom.Name).Count() == 0 && !this[game].Contains(rom))
							{
								Add(game, rom);
							}

							break;
						case ItemType.Sample:
							Sample sample = ((Sample)item).Clone() as Sample;
							sample.CopyMachineInformation(copyFrom);
							if (this[game].Where(i => i.Name == sample.Name).Count() == 0 && !this[game].Contains(sample))
							{
								Add(game, sample);
							}

							break;
					}
				}

				// Now we want to get the parent romof tag and put it in each of the items
				List<DatItem> items = this[game];
				string romof = this[parent][0].RomOf;
				foreach (DatItem item in items)
				{
					item.RomOf = romof;
				}
			}
		}

		/// <summary>
		/// Use cloneof tags to add roms to the parents, removing the child sets in the process
		/// </summary>
		private void AddRomsFromChildren()
		{
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				// Determine if the game has a parent or not
				string parent = null;
				if (!String.IsNullOrEmpty(this[game][0].CloneOf))
				{
					parent = this[game][0].CloneOf;
				}

				// If there is no parent, then we continue
				if (String.IsNullOrEmpty(parent))
				{
					continue;
				}

				// Otherwise, move the items from the current game to a subfolder of the parent game
				DatItem copyFrom = this[parent].Count == 0 ? new Rom { MachineName = parent, Description = parent } : this[parent][0];
				List<DatItem> items = this[game];
				foreach (DatItem item in items)
				{
					// If the disk doesn't have a valid merge tag OR the merged file doesn't exist in the parent, then add it
					if (item.Type == ItemType.Disk && (item.MergeTag == null || !this[parent].Select(i => i.Name).Contains(item.MergeTag)))
					{
						item.CopyMachineInformation(copyFrom);
						Add(parent, item);
					}

					// Otherwise, if the parent doesn't already contain the non-disk, add it
					else if (item.Type != ItemType.Disk && !this[parent].Contains(item))
					{
						// Rename the child so it's in a subfolder
						item.Name = item.Name + "\\" + item.Name;

						// Update the machine to be the new parent
						item.CopyMachineInformation(copyFrom);

						// Add the rom to the parent set
						Add(parent, item);
					}
				}

				// Then, remove the old game so it's not picked up by the writer
				Remove(game);
			}
		}

		/// <summary>
		/// Remove all BIOS and device sets
		/// </summary>
		private void RemoveBiosAndDeviceSets()
		{
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				if (this[game].Count > 0
					&& (this[game][0].MachineType == MachineType.Bios
						|| this[game][0].MachineType == MachineType.Device))
				{
					Remove(game);
				}
			}
		}

		/// <summary>
		/// Use romof tags to remove roms from the children
		/// </summary>
		private void RemoveBiosRomsFromChild()
		{
			// Loop through the romof tags
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				// If the game has no items in it, we want to continue
				if (this[game].Count == 0)
				{
					continue;
				}

				// Determine if the game has a parent or not
				string parent = null;
				if (!String.IsNullOrEmpty(this[game][0].RomOf))
				{
					parent = this[game][0].RomOf;
				}

				// If the parent doesnt exist, we want to continue
				if (String.IsNullOrEmpty(parent))
				{
					continue;
				}

				// If the parent doesn't have any items, we want to continue
				if (this[parent].Count == 0)
				{
					continue;
				}

				// If the parent exists and has items, we remove the items that are in the parent from the current game
				List<DatItem> parentItems = this[parent];
				foreach (DatItem item in parentItems)
				{
					// Figure out the type of the item and remove it accordingly
					switch (item.Type)
					{
						case ItemType.Archive:
							Archive archive = ((Archive)item).Clone() as Archive;
							Remove(game, archive);
							break;
						case ItemType.BiosSet:
							BiosSet biosSet = ((BiosSet)item).Clone() as BiosSet;
							Remove(game, biosSet);
							break;
						case ItemType.Disk:
							Disk disk = ((Disk)item).Clone() as Disk;
							Remove(game, disk);
							break;
						case ItemType.Release:
							Release release = ((Release)item).Clone() as Release;
							Remove(game, release);
							break;
						case ItemType.Rom:
							Rom rom = ((Rom)item).Clone() as Rom;
							Remove(game, rom);
							break;
						case ItemType.Sample:
							Sample sample = ((Sample)item).Clone() as Sample;
							Remove(game, sample);
							break;
					}
				}
			}
		}

		/// <summary>
		/// Use cloneof tags to remove roms from the children
		/// </summary>
		private void RemoveRomsFromChild()
		{
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				// If the game has no items in it, we want to continue
				if (this[game].Count == 0)
				{
					continue;
				}

				// Determine if the game has a parent or not
				string parent = null;
				if (!String.IsNullOrEmpty(this[game][0].CloneOf))
				{
					parent = this[game][0].CloneOf;
				}

				// If the parent doesnt exist, we want to continue
				if (String.IsNullOrEmpty(parent))
				{
					continue;
				}

				// If the parent doesn't have any items, we want to continue
				if (this[parent].Count == 0)
				{
					continue;
				}

				// If the parent exists and has items, we copy the items from the parent to the current game
				List<DatItem> parentItems = this[parent];
				foreach (DatItem item in parentItems)
				{
					// Figure out the type of the item and remove it accordingly
					switch (item.Type)
					{
						case ItemType.Archive:
							Archive archive = ((Archive)item).Clone() as Archive;
							Remove(game, archive);
							break;
						case ItemType.BiosSet:
							BiosSet biosSet = ((BiosSet)item).Clone() as BiosSet;
							Remove(game, biosSet);
							break;
						case ItemType.Disk:
							Disk disk = ((Disk)item).Clone() as Disk;
							Remove(game, disk);
							break;
						case ItemType.Release:
							Release release = ((Release)item).Clone() as Release;
							Remove(game, release);
							break;
						case ItemType.Rom:
							Rom rom = ((Rom)item).Clone() as Rom;
							Remove(game, rom);
							break;
						case ItemType.Sample:
							Sample sample = ((Sample)item).Clone() as Sample;
							Remove(game, sample);
							break;
					}
				}

				// Now we want to get the parent romof tag and put it in each of the items
				List<DatItem> items = this[game];
				string romof = this[parent][0].RomOf;
				foreach (DatItem item in items)
				{
					item.RomOf = romof;
				}
			}
		}

		/// <summary>
		/// Remove all romof and cloneof tags from all games
		/// </summary>
		private void RemoveTagsFromChild()
		{
			List<string> games = Keys.ToList();
			foreach (string game in games)
			{
				List<DatItem> items = this[game];
				foreach (DatItem item in items)
				{
					item.CloneOf = null;
					item.RomOf = null;
				}
			}
		}

		#endregion

		#endregion // Instance Methods
	}
}