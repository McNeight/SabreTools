﻿using SabreTools.Library.Data;

namespace SabreTools.Library.DatItems
{
	/// <summary>
	/// Represents a blank set from an input DAT
	/// </summary>
	public class Blank : DatItem
	{
		#region Constructors

		/// <summary>
		/// Create a default, empty Archive object
		/// </summary>
		public Blank()
		{
			_name = "";
			_itemType = ItemType.Blank;
		}

		#endregion

		#region Cloning Methods

		public override object Clone()
		{
			return new Blank()
			{
				Name = this.Name,
				Type = this.Type,
				Dupe = this.Dupe,

				Supported = this.Supported,
				Publisher = this.Publisher,
				Infos = this.Infos,
				PartName = this.PartName,
				PartInterface = this.PartInterface,
				Features = this.Features,
				AreaName = this.AreaName,
				AreaSize = this.AreaSize,

				MachineName = this.MachineName,
				Comment = this.Comment,
				MachineDescription = this.MachineDescription,
				Year = this.Year,
				Manufacturer = this.Manufacturer,
				RomOf = this.RomOf,
				CloneOf = this.CloneOf,
				SampleOf = this.SampleOf,
				SourceFile = this.SourceFile,
				Runnable = this.Runnable,
				Board = this.Board,
				RebuildTo = this.RebuildTo,
				Devices = this.Devices,
				MachineType = this.MachineType,

				SystemID = this.SystemID,
				System = this.System,
				SourceID = this.SourceID,
				Source = this.Source,
			};
		}

		#endregion

		#region Comparision Methods

		public override bool Equals(DatItem other)
		{
			// If we don't have a blank, return false
			if (_itemType != other.Type)
			{
				return false;
			}

			// Otherwise, treat it as a
			Blank newOther = (Blank)other;

			// If the archive information matches
			return (_machine == newOther._machine);
		}

		#endregion
	}
}
