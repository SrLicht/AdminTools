﻿using CommandSystem;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;
using System;
using UnityEngine;

namespace AdminTools.Commands
{
    using Exiled.API.Features.Items;
    using Exiled.API.Features.Pickups;
    using System.Collections.Generic;

    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    public class DropItem : ICommand, IUsageProvider
    {
        public string Command { get; } = "dropitem";

        public string[] Aliases { get; } = new string[] { "drop", "dropi" };

        public string Description { get; } = "Drops a specified amount of a specified item on either all users or a user";

        public string[] Usage { get; } = new string[] { "%player%", "%item%", "amount" };

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (sender.CheckPermission("at.items"))
            {
                response = "You do not have permission to use this command";
                return false;
            }

            if (arguments.Count != 3)
            {
                response = "Usage: dropitem ((player id/ name) or (all / *)) (ItemType) (amount (200 max for one user, 15 max for all users))";
                return false;
            }

            IEnumerable<Player> players = Player.GetProcessedData(arguments);

            if (arguments.Count != 3)
            {
                response = "Usage: dropitem (all / *) (ItemType) (amount (15 max))";
                return false;
            }

            if (!Enum.TryParse(arguments.At(1), true, out ItemType item))
            {
                response = $"Invalid value for item type: {arguments.At(1)}";
                return false;
            }

            if (!uint.TryParse(arguments.At(2), out uint amount))
            {
                response = $"Invalid amount of item to drop: {arguments.At(2)} {(amount > 30 ? "(\"Try a lower number that won't crash my servers, ty.\" - Galaxy119)" : "")}";
                return false;
            }

            foreach (Player ply in players)
                for (int i = 0; i < amount; i++)
                    Pickup.CreateAndSpawn(item, ply.Position, default, ply);

            response = $"{amount} of {item.ToString()} was spawned on everyone (\"Hehexd\" - Galaxy119)";
            return true;
        }
    }
}
