using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Colors;
using ECommons;
using ECommons.Automation;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Hooks.ActionEffectTypes;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using ECommons.MathHelpers;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Splatoon;
using Splatoon.SplatoonScripting;

namespace Original.M4S
{
	internal class M4S_ElectropeEdge_2 : SplatoonScript
	{
		public override HashSet<uint> ValidTerritories => new() { 1232 };
		public override Metadata? Metadata => new(19, "silverkey");
		private const uint ElectropeEdge_AID = 38341;
		private const uint Condenser_SID = 3999;
		private const uint TileAoE_AID = 38351;
		private const uint SparkII_AID = 38347;
		private const uint Witchgleam_AID = 38790;
		private const uint FourStars_AID = 38352;
		private const uint EightStars_AID = 38353;
		private const uint LeftHaircut_AID = 38380;
		private const uint RightHaircut_AID = 38381;
		private const uint StellarExplosion_AID = 7441;
		private const string FourStars_VFX = "vfx/common/eff/m0888_stlp01";
		private const string EightStars_VFX = "vfx/common/eff/m0888_stlp02";
		private readonly Vector2 center = new(100);
		private int[,] map = new int[5, 5];
		private int electricDamagedCount
		{
			get => witchgleamCount + starsCount;
			set
			{
				witchgleamCount = value;
				starsCount = value;
			}
		}
		private int witchgleamCount;
		private int starsCount;
		private int electropeEdgeCount;
		private int sparkIICount;
		private int process;
		private float rotateAngle;
		private bool gotCondenser;
		private bool isLong;
		private bool isSpread;
		private bool vfxSpawned;
		private bool avoidUnsafeTile;
		private bool moved;
		private Vector2 refPoint = Vector2.Zero;
		private Vector2 myPoint = Vector2.Zero;
		private Vector2? starsPoint;
		private Vector2 avoidUnsafeTilePoint = Vector2.Zero;
		private bool IsLot => isLong ? witchgleamCount > 1 : witchgleamCount > 2;
		private bool HasCondenser => Player.Object.StatusList.Any(x => x.StatusId == Condenser_SID);
		private float CondenserRemainingTime => Player.Object.StatusList.TryGetFirst(x => x.StatusId == Condenser_SID, out var debuff) ? debuff.RemainingTime : 0;
		private IBattleChara? WickedThunder => Svc.Objects.FirstOrDefault(x => x.DataId == 17322) as IBattleChara;

		public override void OnSetup()
		{
			Controller.RegisterElement("Tether", new Element(0) { refX = 100, refY = 100f, thicc = 2f, radius = 0.25f, tether = true });
			Controller.RegisterElement("Message", new Element(1) { radius = 0, overlayBGColor = 3355443200, overlayTextColor = 4294967295, overlayVOffset = 2f, overlayFScale = 2f, refActorType = 1 });
		}

		public override void OnUpdate()
		{
			Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
			if (electropeEdgeCount is not > 1) return;
			if (!gotCondenser)
			{
				if (Player.Object.StatusList.TryGetFirst(x => x.StatusId == Condenser_SID, out var debuff))
				{
					gotCondenser = true;
					isLong = debuff.RemainingTime > 30;
					Chat.Instance.ExecuteCommand($"/e {(isLong ? "long" : "short")} condenser");
				}
			}
			else
			{
				var message = (isLong ? "(Late) " : "(Early) ") + (IsLot ? " (Many)" : " (Few)");
				Vector4? col = HasCondenser && CondenserRemainingTime < 10 ? GradientColor.Get(ImGuiColors.DalamudOrange, ImGuiColors.ParsedPurple, 500) : ((uint)0xFF000000).ToVector4();
				DrawMessage($"{electricDamagedCount} " + message, col);
			}

			if (process == 0)
			{
				var aoeCasters = Svc.Objects.Where(x => x is IBattleChara bc && bc.IsCasting && bc.CastActionId == TileAoE_AID).Cast<IBattleChara>();
				if (aoeCasters.Count() >= 12)
				{
					process = 1;
					foreach (var c in aoeCasters)
					{
						var x = (int)((c.Position.X - 80) / 8);
						var y = (int)((c.Position.Z - 80) / 8);
						map[x, y] = 1;
					}
				}
				else
				{
					UpdateStars();
				}
			}
			else if (process == 1)
			{
				bool found = false;
				for (var x = 0; x < 5; x++)
				{
					for (var y = 0; y < 5; y++)
					{
						if (map[x, y] == 0) continue;
						bool isRef = true;
						(int, int)[] offsets = { new(-1, 0), new(0, -1), new(1, 0), new(0, 1) };
						foreach (var offset in offsets)
						{
							int cx = x + offset.Item1;
							int cy = y + offset.Item2;
							if (cx < 0 || cy < 0 || cx > 4 || cy > 4) continue;
							if (map[cx, cy] == 0)
							{
								isRef = false;
								break;
							}
						}
						if (isRef)
						{
							var refX = x * 8 + 84;
							var refY = y * 8 + 84;
							rotateAngle = (MathF.PI * 2 * 3 / 4) - MathF.Atan2(refY - center.Y, refX - center.X);
							refPoint = Rotate(new Vector2(refX, refY), center, rotateAngle);
							found = true;
							break;
						}
					}
					if (found)
					{
						process = 2;
						break;
					}
				}
			}
			else if (process == 2)
			{
				bool isDPS = Player.Object.GetRole() == CombatRole.DPS;
				bool isDebuff = HasCondenser && CondenserRemainingTime < 10;
				if (isDebuff)
				{
					myPoint.X = refPoint.X + (isDPS ? 16 : -16);
					myPoint.Y = refPoint.Y + (electricDamagedCount > 2 ? 0 : 24);
					myPoint = AlignCloser(Rotate(myPoint, center, -rotateAngle));
				}
				else
				{
					if (Conf.EnableMeleeUptime)
					{
						myPoint.X = refPoint.X - 3.9f;
						myPoint.Y = refPoint.Y + 20.5f;
						avoidUnsafeTilePoint = new Vector2(myPoint.X - 0.5f, myPoint.Y);
						myPoint = Rotate(myPoint, center, -rotateAngle);
						avoidUnsafeTilePoint = Rotate(avoidUnsafeTilePoint, center, -rotateAngle);
						avoidUnsafeTile = true;
					}
					else
					{
						myPoint.X = refPoint.X + 0;
						myPoint.Y = refPoint.Y + 32;
						myPoint = AlignCloser(Rotate(myPoint, center, -rotateAngle));
					}
				}
				process = 3;
			}
			else if (process == 3)
			{
				DrawTether(myPoint, Conf.AM_AvoidTile || Conf.AM_MeleeUptime && avoidUnsafeTile);
			}
		}

		private void UpdateStars()
		{
			if (!vfxSpawned) return;
			var wickedThunder = this.WickedThunder;
			if (wickedThunder is null) return;
			if (wickedThunder.IsCasting && wickedThunder.CastActionId.EqualsAny(LeftHaircut_AID, RightHaircut_AID))
			{
				bool isLeft = wickedThunder.CastActionId == LeftHaircut_AID;
				bool isSpread = this.isSpread;
				bool isLot = this.IsLot;
				Vector2? p = null;
				switch (GConf.MyPosition)
				{
					case GlobalConfig.Position.MT:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 93.9f : 106.1f, 94.3f);
							else
								p = isLot ? new Vector2(isLeft ? 99.9f : 100.1f, 96.9f) : new Vector2(isLeft ? 99.9f : 100.1f, 103.1f);
						}
						break;
					case GlobalConfig.Position.ST:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 91.7f : 108.3f, 100.1f);
							else
								p = isLot ? new Vector2(isLeft ? 99.9f : 100.1f, 96.9f) : new Vector2(isLeft ? 99.9f : 100.1f, 103.1f);
						}
						break;
					case GlobalConfig.Position.H1:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 85f : 115f, 95f);
							else
								p = isLot ? new Vector2(isLeft ? 99.9f : 100.1f, 96.9f) : new Vector2(isLeft ? 99.9f : 100.1f, 103.1f);
						}
						break;
					case GlobalConfig.Position.H2:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 99.9f : 100.1f, 95.8f);
							else
								p = isLot ? new Vector2(isLeft ? 99.9f : 100.1f, 96.9f) : new Vector2(isLeft ? 99.9f : 100.1f, 103.1f);
						}
						break;
					case GlobalConfig.Position.D1:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 94.2f : 105.8f, 105.9f);
							else
								p = isLot ? new Vector2(isLeft ? 93.5f : 106.5f, 95.5f) : new Vector2(isLeft ? 93.5f : 106.5f, 104.5f);
						}
						break;
					case GlobalConfig.Position.D2:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 99.9f : 100.1f, 108.3f);
							else
								p = isLot ? new Vector2(isLeft ? 93.5f : 106.5f, 95.5f) : new Vector2(isLeft ? 93.5f : 106.5f, 104.5f);
						}
						break;
					case GlobalConfig.Position.D3:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 85f : 115f, 105f);
							else
								p = isLot ? new Vector2(isLeft ? 93.5f : 106.5f, 95.5f) : new Vector2(isLeft ? 93.5f : 106.5f, 104.5f);
						}
						break;
					case GlobalConfig.Position.D4:
						{
							if (isSpread)
								p = new Vector2(isLeft ? 99.5f : 100.5f, 102f);
							else
								p = isLot ? new Vector2(isLeft ? 93.5f : 106.5f, 95.5f) : new Vector2(isLeft ? 93.5f : 106.5f, 104.5f);
						}
						break;
				}
				if (p != null)
				{
					starsPoint = p;
					DrawTether((Vector2)starsPoint, false);
				}
			}
		}

		private Vector2 Rotate(Vector2 p, Vector2 center, float angle)
		{
			angle = MathF.Atan2(p.Y - center.Y, p.X - center.X) + angle;
			var dis = Vector2.Distance(p, center);
			var nx = dis * MathF.Cos(angle) + center.X;
			var ny = dis * MathF.Sin(angle) + center.Y;
			return new Vector2(nx, ny);
		}

		private Vector2 AlignCloser(Vector2 p)
		{
			float xDiff = p.X - center.X;
			float yDiff = p.Y - center.Y;
			float xOffset = p.X.InRange(99, 101) ? 0 : xDiff > 0 ? -3.75f : 3.75f;
			float yOffset = p.Y.InRange(99, 101) ? 0 : yDiff > 0 ? -3.75f : 3.75f;
			return new Vector2(p.X + xOffset, p.Y + yOffset);
		}

		private void DrawTether(Vector2 pos, bool posMove = false)
		{
			if (!moved)
			{
				moved = true;
				PosMove(pos, posMove);
			}
			if (Controller.TryGetElementByName("Tether", out var e))
			{
				e.Enabled = true;
				e.refX = pos.X;
				e.refY = pos.Y;
				e.color = ImGuiColors.ParsedGreen.ToUint();
			}
		}

		private void PosMove(Vector2 pos, bool posMove = false)
		{
			if (Conf.EnableAutoMove && posMove)
			{
				Chat.Instance.ExecuteCommand($"/pos m {pos.X} {pos.Y}");
				Chat.Instance.ExecuteCommand($"/e pos m {pos.X} {pos.Y} by {nameof(M4S_ElectropeEdge_2)}");
			}
			//DuoLog.Information($"/pos m {pos.X} {pos.Y}");
		}

		private void DrawMessage(string message, Vector4? color = null)
		{
			if (Controller.TryGetElementByName("Message", out var e))
			{
				e.Enabled = true;
				e.overlayText = message;
				if (color is not null) e.overlayBGColor = ((Vector4)color).ToUint();
			}
		}

		public override void OnVFXSpawn(uint target, string vfxPath)
		{
			switch (vfxPath)
			{
				case string path when path.ContainsAny(FourStars_VFX, EightStars_VFX) && electropeEdgeCount == 2:
					{
						isSpread = vfxPath.Contains(EightStars_VFX);
						vfxSpawned = true;
					}
					break;
			}
		}

		public override void OnActionEffectEvent(ActionEffectSet set)
		{
			switch (set.Action?.RowId)
			{
				case ElectropeEdge_AID:
					{
						electropeEdgeCount++;
					}
					break;
				case Witchgleam_AID when HasCondenser && electropeEdgeCount == 2:
					{
						foreach (var tf in set.TargetEffects)
						{
							if (tf.TargetID != Player.Object.GameObjectId) continue;
							witchgleamCount++;
						}
					}
					break;
				case FourStars_AID or EightStars_AID when electropeEdgeCount == 2:
					{
						moved = false;
						if (HasCondenser)
						{
							foreach (var tf in set.TargetEffects)
							{
								if (tf.TargetID != Player.Object.GameObjectId) continue;
								starsCount++;
							}
						}
					}
					break;
				case SparkII_AID when avoidUnsafeTile && electropeEdgeCount == 2:
					{
						sparkIICount++;
						if (sparkIICount >= 2)
						{
							avoidUnsafeTile = false;
							PosMove(avoidUnsafeTilePoint, Conf.AM_MeleeUptime);
						}
					}
					break;
				case TileAoE_AID when electropeEdgeCount == 2:
					{
						Init();
						if (FakeParty.Get().All(x => x.StatusList.All(z => z.StatusId != Condenser_SID)))
						{
							this.OnReset();
						}
					}
					break;
				case StellarExplosion_AID when electropeEdgeCount == 2:
					{
						if (starsPoint != null) PosMove((Vector2)starsPoint, Conf.AM_Stars);
					}
					break;
			}
		}

		private void Init()
		{
			process = 0;
			sparkIICount = 0;
			refPoint = Vector2.Zero;
			myPoint = Vector2.Zero;
			avoidUnsafeTilePoint = Vector2.Zero;
			rotateAngle = 0f;
			moved = false;
			avoidUnsafeTile = false;
			map = new int[5, 5];
		}

		public override void OnReset()
		{
			Init();
			electricDamagedCount = 0;
			electropeEdgeCount = 0;
			gotCondenser = false;
			isLong = false;
			isSpread = false;
			vfxSpawned = false;
			starsPoint = null;
		}

		public override void OnSettingsDraw()
		{
			ImGui.SetNextItemWidth(75);
			GConf.Save(ImGuiEx.EnumCombo("My Position", ref GConf.MyPosition));
			using (DisabledBlock.Begin(!ImGui.GetIO().KeyCtrl))
			{
				ImGui.Checkbox("Enable AutoMove", ref Conf.EnableAutoMove);
			}
			if (Conf.EnableAutoMove)
			{
				Prefix(false);
				ImGui.Checkbox("Avoid Tile", ref Conf.AM_AvoidTile);
				Prefix(false);
				ImGui.Checkbox("Melee Uptime", ref Conf.AM_MeleeUptime);
				Prefix(true);
				ImGui.Checkbox("Stars", ref Conf.AM_Stars);
			}
			using (DisabledBlock.Begin(!ImGui.GetIO().KeyCtrl))
			{
				ImGui.Checkbox("Enable MeleeUptime", ref Conf.EnableMeleeUptime);
			}
			if (ImGui.CollapsingHeader("Debug"))
			{
				ImGui.Text($"Electrope Edge: {electropeEdgeCount}");
				ImGui.SameLine();
				if (ImGui.SmallButton("-")) electropeEdgeCount--;
				ImGui.SameLine();
				if (ImGui.SmallButton("+")) electropeEdgeCount++;
				ImGui.Text($"Process: {process}");
				ImGui.Text($"Ref: {refPoint}, Rotate: {rotateAngle}");
				ImGui.Text($"My Point: {myPoint}");
				ImGui.Text($"Vfx Spawned: {vfxSpawned}");
				ImGui.Text($"Is Spread: {isSpread}");
				for (var y = 0; y < 5; y++)
				{
					ImGui.Text($"{map[0, y]}, {map[1, y]}, {map[2, y]}, {map[3, y]}, {map[4, y]}");
				}
			}
		}

		private readonly GlobalConfig GConf = GlobalConfig.Load();
		private Config Conf => Controller.GetConfig<Config>();
		public class Config : IEzConfig
		{
			public bool EnableAutoMove = false;
			public bool EnableMeleeUptime = false;
			public bool AM_AvoidTile = false;
			public bool AM_MeleeUptime = true;
			public bool AM_Stars = true;
		}

		private static void AddTextCentered(Vector2 pos, string text, uint color)
		{
			var textSize = ImGui.CalcTextSize(text);
			ImGui.GetWindowDrawList().AddText(pos - textSize / 2, color, text);
		}
		private void Prefix(string prefix = "◇")
		{
			var dummySize = new Vector2(ImGui.GetFrameHeight());
			ImGui.Dummy(dummySize);
			AddTextCentered(ImGui.GetItemRectMin() + dummySize / 2, prefix, ImGui.GetColorU32(ImGuiCol.Text));
			ImGui.SameLine();
		}
		private void Prefix(bool isLast) => Prefix(isLast ? "└" : "├");

		private sealed class DisabledBlock : IDisposable
		{
			private static readonly DisabledBlock instance = new();
			private DisabledBlock() { }

			public static DisabledBlock Begin(bool conditional = true)
			{
				ImGui.BeginDisabled(conditional);
				return instance;
			}

			public void Dispose() => ImGui.EndDisabled();
		}
	}

	public class GlobalConfig
	{
		[JsonConverter(typeof(StringEnumConverter))]
		public GlobalConfig.Position MyPosition;
		private static string FullFilePath => Path.Combine(Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "Scripts"), nameof(GlobalConfig) + ".json");
		private static readonly object LockObject = new();
		public static GlobalConfig Load()
		{
			if (!File.Exists(FullFilePath)) return new GlobalConfig();
			var json = File.ReadAllText(FullFilePath);
			return JsonConvert.DeserializeObject<GlobalConfig>(json) ?? new GlobalConfig();
		}
		public void Save(bool save)
		{
			if (!save) return;
			Task.Run(delegate
			{
				try
				{
					lock (LockObject)
					{
						var json = JsonConvert.SerializeObject(this);
						File.WriteAllText(FullFilePath, json);
					}
				}
				catch (Exception e)
				{
					DuoLog.Error($"[{nameof(GlobalConfig)}] {e.Message}");
				}
			});
		}
		public enum Position { MT, ST, H1, H2, D1, D2, D3, D4 }
	}
}
