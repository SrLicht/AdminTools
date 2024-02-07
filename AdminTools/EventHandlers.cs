namespace AdminTools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Exiled.API.Enums;
    using Exiled.API.Features;
    using MEC;
    using Mirror;
    using UnityEngine;
    using Exiled.API.Extensions;
    using Exiled.API.Features.Items;
    using Exiled.API.Features.Doors;
    using Exiled.Events.EventArgs.Player;
    using Exiled.Events.EventArgs.Server;
    using InventorySystem.Items.Firearms.Attachments;
    using PlayerRoles;
    using Utils.NonAllocLINQ;
    using Exiled.API.Interfaces;
    using Log = Exiled.API.Features.Log;
    using Object = UnityEngine.Object;
    using Exiled.API.Features.Pickups.Projectiles;

    public class EventHandlers
	{
		private readonly Main plugin;
		public EventHandlers(Main main) => plugin = main;

		public void OnDoorOpen(InteractingDoorEventArgs ev)
		{
			if (Main.PryGateHubs.Contains(ev.Player) && ev.Door is Gate gate)
                gate.TryPry();
		}

		public static string FormatArguments(ArraySegment<string> sentence, int index)
		{
			StringBuilder sb = new();
			foreach (string word in sentence.Segment(index))
			{
				sb.Append(word);
				sb.Append(" ");
			}
			string msg = sb.ToString();
			return msg;
		}

        public void OnPlayerDestroying(DestroyingEventArgs ev)
        {
			if (Main.RoundStartMutes.Remove(ev.Player))
            {
				ev.Player.IsMuted = false;
            }
        }

        public static void SpawnWorkbench(Player ply, Vector3 position, Vector3 rotation, Vector3 size, out int benchIndex)
		{
			try
			{
				Log.Debug($"Spawning workbench");
				benchIndex = 0;
				GameObject bench =
					Object.Instantiate(
						NetworkManager.singleton.spawnPrefabs.Find(p => p.gameObject.name == "Work Station"));
				rotation.x += 180;
				rotation.z += 180;
                Offset offset = new()
                {
                    position = position,
                    rotation = rotation,
                    scale = Vector3.one,
                };
                bench.gameObject.transform.localScale = size;
				NetworkServer.Spawn(bench);
				if (Main.BchHubs.TryGetValue(ply, out List<GameObject> objs))
				{
					objs.Add(bench);
				}
				else
				{
					Main.BchHubs.Add(ply, new());
					Main.BchHubs[ply].Add(bench);
					benchIndex = Main.BchHubs[ply].Count();
				}

				if (benchIndex != 1)
					benchIndex = objs.Count();
				bench.transform.localPosition = offset.position;
				bench.transform.localRotation = Quaternion.Euler(offset.rotation);
				bench.AddComponent<WorkstationController>();
			}
			catch (Exception e)
			{
				Log.Error($"{nameof(SpawnWorkbench)}: {e}");
				benchIndex = -1;
			}
		}

        public static IEnumerator<float> DoRocket(Player player, float speed)
		{
			const int maxAmnt = 50;
			int amnt = 0;
			while (player.IsAlive)
			{
				player.Position += Vector3.up * speed;
				amnt++;
				if (amnt >= maxAmnt)
				{
					player.IsGodModeEnabled = false;
					if (Projectile.CreateAndSpawn(ProjectileType.FragGrenade, player.Position, player.Rotation).Is(out TimeGrenadeProjectile timeGrenadeProjectile))
						timeGrenadeProjectile.Explode();
                    player.Kill("Went on a trip in their favorite rocket ship.");
				}

				yield return Timing.WaitForOneFrame;
			}
		}

		public static void DoJail(Player player, bool skipadd = false)
		{
			if (!skipadd)
            {
                Main.JailedPlayers.Add(new Jailed
				{
					Health = player.Health,
                    RelativePosition = player.RelativePosition,
					Items = player.Items.ToList(),
					Effects = player.ActiveEffects.Select(x => new Effect(x)).ToList(),
					Name = player.Nickname,
					Role = player.Role,
					Userid = player.UserId,
					CurrentRound = true,
					Ammo = player.Ammo.ToDictionary(x => x.Key.GetAmmoType(), x => x.Value),
                });
			}

			if (player.IsOverwatchEnabled)
				player.IsOverwatchEnabled = false;
			player.Ammo.Clear();
			player.Inventory.SendAmmoNextFrame = true;

            player.ClearInventory(false);
			player.Role.Set(RoleTypeId.Tutorial, RoleSpawnFlags.UseSpawnpoint);
		}

		public static void DoUnJail(Player player)
		{
			Jailed jail = Main.JailedPlayers.Find(j => j.Userid == player.UserId);
			if (jail.CurrentRound)
			{
				player.Role.Set(jail.Role, RoleSpawnFlags.None);
				try
				{
					player.ResetInventory(jail.Items);
                    player.Health = jail.Health;
					player.Position = jail.RelativePosition.Position;
					foreach (KeyValuePair<AmmoType, ushort> kvp in jail.Ammo)
						player.Ammo[kvp.Key.GetItemType()] = kvp.Value;
					player.SyncEffects(jail.Effects);

                    player.Inventory.SendItemsNextFrame = true;
                    player.Inventory.SendAmmoNextFrame = true;
                }
                catch (Exception e)
				{
					Log.Error($"{nameof(DoUnJail)}: {e}");
				}
			}
			else
			{
				player.Role.Set(RoleTypeId.Spectator, RoleSpawnFlags.UseSpawnpoint);
			}
			Main.JailedPlayers.Remove(jail);
		}

		public void OnPlayerVerified(VerifiedEventArgs ev)
		{
			try
			{
				if (Main.JailedPlayers.Any(j => j.Userid == ev.Player.UserId))
					DoJail(ev.Player, true);

				if (File.ReadAllText(plugin.OverwatchFilePath).Contains(ev.Player.UserId))
				{
					Log.Debug($"Putting {ev.Player.UserId} into overwatch.");
					ev.Player.IsOverwatchEnabled = true;
				}

				if (File.ReadAllText(plugin.HiddenTagsFilePath).Contains(ev.Player.UserId))
				{
					Log.Debug($"Hiding {ev.Player.UserId}'s tag.");
					Timing.CallDelayed(Timing.WaitForOneFrame, () => ev.Player.BadgeHidden = true);
				}

				if (Main.RoundStartMutes.Count != 0 && !ev.Player.ReferenceHub.serverRoles.RemoteAdmin && !Main.RoundStartMutes.Contains(ev.Player))
                {
					Log.Debug($"Muting {ev.Player.UserId} (no RA).");
					ev.Player.IsMuted = true;
					Main.RoundStartMutes.Add(ev.Player);
                }
			}
			catch (Exception e)
			{
				Log.Error($"Player Join: {e}");
			}
		}

		public void OnRoundStart()
		{
			foreach (Player ply in Main.RoundStartMutes)
			{
				if (ply != null)
				{
					ply.IsMuted = false;
				}
			}
			Main.RoundStartMutes.Clear();
		}

		public void OnRoundEnd(RoundEndedEventArgs ev)
		{
			try
			{
				List<string> overwatchRead = File.ReadAllLines(plugin.OverwatchFilePath).ToList();
				List<string> tagsRead = File.ReadAllLines(plugin.HiddenTagsFilePath).ToList();

				foreach (Player player in Player.List)
				{
					string userId = player.UserId;

					if (player.IsOverwatchEnabled && !overwatchRead.Contains(userId))
						overwatchRead.Add(userId);
					else if (!player.IsOverwatchEnabled && overwatchRead.Contains(userId))
						overwatchRead.Remove(userId);

					if (player.BadgeHidden && !tagsRead.Contains(userId))
						tagsRead.Add(userId);
					else if (!player.BadgeHidden && tagsRead.Contains(userId))
						tagsRead.Remove(userId);
				}

				foreach (string s in overwatchRead)
					Log.Debug($"{s} is in overwatch.");
				foreach (string s in tagsRead)
					Log.Debug($"{s} has their tag hidden.");
				File.WriteAllLines(plugin.OverwatchFilePath, overwatchRead);
				File.WriteAllLines(plugin.HiddenTagsFilePath, tagsRead);

				// Update all the jails that it is no longer the current round, so when they are unjailed they don't teleport into the void.
				foreach (Jailed jail in Main.JailedPlayers)
				{
					if(jail.CurrentRound)
						jail.CurrentRound = false;
				}
			}
			catch (Exception e)
			{
				Log.Error($"Round End: {e}");
			}
		}

		public void OnTriggerTesla(TriggeringTeslaEventArgs ev)
		{
			if (ev.Player.IsGodModeEnabled)
				ev.IsAllowed = false;
		}

		public void OnSetClass(ChangingRoleEventArgs ev)
		{
			if (plugin.Config.GodTuts)
				ev.Player.IsGodModeEnabled = ev.NewRole is RoleTypeId.Tutorial;
		}

		public void OnWaitingForPlayers()
		{
			Main.IK.Clear();
            Main.BreakDoors.Clear();
		}

		public void OnPlayerInteractingDoor(InteractingDoorEventArgs ev)
		{
			if (Main.BreakDoors.Contains(ev.Player) && ev.Door is IDamageableDoor damageableDoor)
                damageableDoor.Break();
        }
	}
}
