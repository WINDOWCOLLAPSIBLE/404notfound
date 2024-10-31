using System;
using System.Collections.Generic;
using System.Linq;
using Atropos.Core;
using Atropos.Data;
using Atropos.Logging;
using Atropos.Module;
using Atropos.Network;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Colors;
using ECommons.Configuration;
using ECommons.Hooks.ActionEffectTypes;
using ECommons.ImGuiMethods;
using ECommons.MathHelpers;
using ImGuiNET;

namespace Atropos.AtroposScripts.M3S
{
	public class M3S_StatusOffManager : AtroposScript
	{
		public override HashSet<uint> ValidTerritories => [1230];
		public override Metadata? Metadata => new Metadata(15, "silverkeyyyy");
		private Dictionary<uint, bool> _wasCritTable = [];
		private Dictionary<uint, long> _removedStatusTable = [];
		private Dictionary<string, bool> _specificTable = [];
		private Arguments arguments = new();
		private long combatTime => Controller.CombatTimeMilliSeconds;
		private bool log => Conf.Log;
		private IPlayerCharacter Player => ECommons.GameHelpers.Player.Object;
		private static class Constants
		{
			internal const string Troubadour55s = "Troubadour55s";

			internal const string LongFuse_Key = "IsLongFuse";
			internal const string LongFuseVFX = "vfx/common/eff/x6r3_stlp_longpc_c0e1.avfx";
			internal const string ShortFuseVFX = "vfx/common/eff/x6r3_stlp_shortpc_c0e1.avfx";

			internal const string ProximityDive_Key = "IsProximityDive";
			internal static readonly HashSet<uint> ProximityDive_AID = [37877, 37868];
			internal static readonly HashSet<uint> KnockbackDive_AID = [37878, 37869];

			internal const string ChariotLariat_Key = "IsChariotLariat";
			internal static readonly HashSet<uint> ChariotLariat_AID = [37864, 37866];
			internal static readonly HashSet<uint> DynamoLariat_AID = [37865, 37867];
		}

		public override void OnUpdate()
		{
			//Update_Troubadour55s();
		}

		private void Update_Troubadour55s()
		{
		}

		private class Arguments : IArguments
		{
			public Dictionary<uint, bool> wasCritTable = [];
			public Dictionary<uint, (bool Compare, long TimeRange)> removedStatusTable = [];
			public Dictionary<string, bool> specificTable = [];
		}

		public override void OnEnable()
		{
			AutoStatusOff.StatusOffEvent += OnStatusOffEvent;
			NetworkManager.StatusTracker.OnStatusGain += OnStatusGain;
			AtroposLog.Information($"[{nameof(M3S_StatusOffManager)}] Enabled.");
		}

		public override void OnDisable()
		{
			AutoStatusOff.StatusOffEvent -= OnStatusOffEvent;
			NetworkManager.StatusTracker.OnStatusGain -= OnStatusGain;
			AtroposLog.Information($"[{nameof(M3S_StatusOffManager)}] Disabled.");
		}

		private void OnStatusGain(uint sourceActorId, uint actorId, uint statusId, float duration, int stacks)
		{
		}

		public override void RecieveArguments(string s, int index)
		{
			var args = JsonConvertToArguments<Arguments>(s, log);
			arguments = args ?? new Arguments();
		}

		public override bool GetBool()
		{
			var ret = true;
			foreach (var x in arguments.wasCritTable)
			{
				ret &= _wasCritTable.TryGetValue(x.Key, out var v) ? x.Value == v : false;
			}
			foreach (var x in arguments.removedStatusTable)
			{
				if (_removedStatusTable.TryGetValue(x.Key, out var v) && M3S_StatusOffManager.InRange(v, combatTime - x.Value.TimeRange, combatTime))
					ret &= x.Value.Compare == true;
				else
					ret &= x.Value.Compare == false;
			}
			foreach (var x in arguments.specificTable)
			{
				ret &= _specificTable.TryGetValue(x.Key, out var v) ? x.Value == v : x.Value == false;
			}
			return ret;
		}

		private static bool InRange(long f, long inclusiveStart, long inclusiveEnd)
		{
			return f >= inclusiveStart && f <= inclusiveEnd;
		}

		public override void OnActionEffectEvent(ActionEffectSet set)
		{
			if (set.Action == null) return;
			switch ((AID)set.Action.RowId)
			{
				case AID.Celestial_Intersection or AID.Consolation or AID.Helios_Conjunction or AID.Star_Prism_Heal or AID.Lady_of_Crowns or AID.Essential_Dignity or AID.Second_Wind:
					{
						foreach (var tg in set.TargetEffects)
						{
							if (tg.TargetID != Player.GameObjectId) continue;
							tg.ForEach(e =>
							{
								if (e.type == ActionEffectType.Heal)
								{
									bool isCrit = e.param1 == 32;
									_wasCritTable[set.Action.RowId] = isCrit;
									Log($"{set.Action.Name} {(isCrit ? "Critical" : "Normal")} {e.param1}");
								}
							});
						}
					}
					break;
			}
		}

		public override void OnCastStart(IBattleChara chara, uint castActionId)
		{
			switch (castActionId)
			{
				case var id when Constants.ProximityDive_AID.Contains(id):
					{
						_specificTable[Constants.ProximityDive_Key] = true;
						SpecificTable.Set(Constants.ProximityDive_Key, true);
					}
					break;
				case var id when Constants.KnockbackDive_AID.Contains(id):
					{
						_specificTable[Constants.ProximityDive_Key] = false;
						SpecificTable.Set(Constants.ProximityDive_Key, false);
					}
					break;
				case var id when Constants.ChariotLariat_AID.Contains(id):
					{
						_specificTable[Constants.ChariotLariat_Key] = true;
						SpecificTable.Set(Constants.ChariotLariat_Key, true);
					}
					break;
				case var id when Constants.DynamoLariat_AID.Contains(id):
					{
						_specificTable[Constants.ChariotLariat_Key] = false;
						SpecificTable.Set(Constants.ChariotLariat_Key, false);
					}
					break;
			}
		}

		public override void OnVfxSpawn(uint objectId, string vfxPath)
		{
			if (objectId != Player.EntityId) return;
			switch (vfxPath)
			{
				case Constants.ShortFuseVFX:
					{
						_specificTable[Constants.LongFuse_Key] = false;
						SpecificTable.Set(Constants.LongFuse_Key, false);
					}
					break;
				case Constants.LongFuseVFX:
					{
						_specificTable[Constants.LongFuse_Key] = true;
						SpecificTable.Set(Constants.LongFuse_Key, true);
					}
					break;
			}
		}

		private void OnStatusOffEvent(uint statusId, string statusName, long combatTime)
		{
			Log($"{statusName} off at {combatTime / 1000f}s({combatTime}ms).");
			_removedStatusTable[statusId] = combatTime;
		}

		private bool IsPartyMember(IGameObject obj) =>
			obj is IPlayerCharacter pc && Utils.GetTrueParty().Select(x => x.GameObjectId).Contains(pc.GameObjectId);

		private void Log(string message)
		{
			if (!log) return;
			AtroposLog.Debug($"[{nameof(M3S_StatusOffManager)}] {message}", LogAttribute.AutoStatusOff);
		}

		public override void OnReset()
		{
			_removedStatusTable.Clear();
			_wasCritTable.Clear();
			_specificTable.Clear();
		}

		private Config Conf => Controller.GetConfig<Config>();
		public class Config : IEzConfig
		{
			public bool Log = true;

			public List<(Position Position, string Name)> Priority = new List<(Position, string)>()
			{
				(Position.H1, "Strawberry Cheesetart"), (Position.H2, "Hi-uni Star"), (Position.MT, "Chichiri Highwind"), (Position.ST, "Kara Agekun"),
				(Position.D3, "Soren Kuroyuri"), (Position.D4, "Miq Nonne"), (Position.D1, "Metal Kuraa"), (Position.D2, "Silverkey Ashen'one")
			};
			public SortedDictionary<Position, int> SetHp = new SortedDictionary<Position, int>()
			{
				{Position.MT, 0},
				{Position.ST, 0},
				{Position.H1, 69549},
				{Position.H2, 69549},
				{Position.D1, 0},
				{Position.D2, 0},
				{Position.D3, 66127},
				{Position.D4, 68720},
			};
		}

		public enum Position { MT, ST, H1, H2, D1, D2, D3, D4 }

		public override void OnSettingsDraw()
		{
			if (ImGui.CollapsingHeader("Debug"))
			{
				ImGui.Checkbox("Log", ref Conf.Log);
				ImGui.Separator();
				ImGuiEx.Text(EColor.YellowBright, "WasCrit Table");
				foreach (var x in _wasCritTable)
				{
					ImGui.Text($"[{x.Key}] {x.Key.GetActionName()}, {x.Value}");
				}
				ImGuiEx.Text(EColor.YellowBright, "Arguments");
				foreach (var x in arguments.wasCritTable)
				{
					ImGui.Text($"[{x.Key}] {x.Key.GetActionName()}, {x.Value}");
					ImGui.SameLine();
					bool b = _wasCritTable.TryGetValue(x.Key, out var v) ? x.Value == v : x.Value == false;
					ImGuiEx.Text(b ? EColor.GreenBright : ImGuiColors.DalamudGrey, b ? $"Match" : "No match");
				}
				ImGui.Separator();
				ImGuiEx.Text(EColor.YellowBright, "RemovedStatus Table");
				foreach (var x in _removedStatusTable)
				{
					ImGui.Text($"{x.Key.GetStatusName()}, {x.Value}");
				}
				ImGuiEx.Text(EColor.YellowBright, "Arguments");
				foreach (var x in arguments.removedStatusTable)
				{
					ImGui.Text($"{x.Key.GetStatusName()}, {x.Value}");
					ImGui.SameLine();
					bool b = _removedStatusTable.TryGetValue(x.Key, out var v) && v.InRange(combatTime - x.Value.TimeRange, combatTime) ? x.Value.Compare == true : x.Value.Compare == false;
					ImGuiEx.Text(b ? EColor.GreenBright : ImGuiColors.DalamudGrey, b ? $"Match" : "No match");
				}
				ImGui.Separator();
				ImGuiEx.Text(EColor.YellowBright, "Specific Table");
				foreach (var x in _specificTable)
				{
					ImGui.Text($"{x.Key}, {x.Value}");
				}
				ImGuiEx.Text(EColor.YellowBright, "Arguments");
				foreach (var x in arguments.specificTable)
				{
					ImGui.Text($"{x.Key}, {x.Value}");
					bool b = _specificTable.TryGetValue(x.Key, out var v) ? x.Value == v : x.Value == false;
					ImGuiEx.Text(b ? EColor.GreenBright : ImGuiColors.DalamudGrey, b ? $"Match" : "No match");
				}
			}
		}
	}
}