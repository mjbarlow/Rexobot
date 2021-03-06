﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Rexobot.Gumroad;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Rexobot.Commands
{
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.ManageRoles)]
    public class AdminModule : BotModuleBase
    {
        private readonly IConfiguration _config;
        private readonly IGumroadApi _gumroad;
        private readonly RootDatabase _db;
        private readonly ResponsiveService _responsive;

        public AdminModule(IConfiguration config, IGumroadApi gumroad, RootDatabase db, ResponsiveService responsive)
        {
            _config = config;
            _gumroad = gumroad;
            _db = db;
            _responsive = responsive;
        }

        [Command("products")]
        [Summary("Show a list of all products and their ids")]
        [Remarks("No parameters")]
        public async Task ShowProductsAsync()
        {
            var products = (await _gumroad.GetProductsAsync(_config["gumroad:token"])).Products;
            string allProducts = string.Join(Environment.NewLine, products.Select(x => $"{x.Name} `{x.Id}`"));
            await ReplyAsync("**Available Products**\n" + allProducts);
        }

        [Command("createrolesync"), Alias("newrolesync", "addrolesync")]
        [Summary("Add a product to the role sync system")]
        [Remarks("1st Parameter: Role to sync\n" +
                 "2nd Parameter: Product Id\n" +
                 "3rd Parameter (optional): The id of the message to watch for reactions")]
        public async Task CreateRoleSyncAsync(SocketRole socketRole, string productId, ulong? watchMessageId = null)
        {
            var result = await _gumroad.GetProductAsync(_config["gumroad:token"], productId);
            if (!result.IsSuccess)
            {
                await ReplyAsync("Sorry, I couldn't find any products matching that ID.");
                return;
            }

            var product = new RexoProduct
            {
                Id = result.Product.Id,
                Name = result.Product.Name,
                PreviewImageUrl = result.Product.PreviewUrl,
                ShortUrl = result.Product.ShortUrl,
                GuildId = Context.Guild.Id,
                RoleId = socketRole.Id,
                WatchMessageId = watchMessageId
            };
            _db.Products.Add(product);
            _db.SaveChanges();

            await ReplyAsync($"Successfully added `{product.Name}` to the syncing service!");
        }

        [Command("removerolesync"), Alias("deleterolesync", "delrolesync")]
        [Summary("Remove a product from the role syncing system")]
        [Remarks("1st Parameter: The name or id of a linked product")]
        public async Task RemoveRoleSyncAsync(RexoProduct product)
        {
            await ReplyAsync($"Are you sure you want to remove `{product.Name}` from the sycning service? Reply yes/no");
            var response = await _responsive.WaitForMessageAsync((msg) =>
            {
                return msg.Content.ToLower() == "yes";
            });

            if (response == null)
                return;

            _db.Remove(product);
            _db.SaveChanges();

            await ReplyAsync($"Successfully removed `{product.Name}` from the syncing service!");
        }

        [Command("createsyncmessage"), Alias("createsyncmsg", "newsyncmsg", "addsyncmsg")]
        [Summary("Have the bot create a message for you in the specified channel for role sync")]
        [Remarks("1st Parameter: The name or id of a product\n" +
                 "2nd Parameter: The text channel to post the message in" +
                 "3rd Parameter (optional): A custom body message")]
        public async Task CreateSyncMessageAsync(RexoProduct product, SocketTextChannel channel, [Remainder]string message = null)
        {
            var role = Context.Guild.GetRole(product.RoleId);

            var embed = new EmbedBuilder()
                .WithTitle(product.Name)
                .WithThumbnailUrl(product.PreviewImageUrl)
                .WithUrl(product.ShortUrl)
                .WithDescription(message != null ? message : $"If you would like to sync your Gumroad purchase for this product to Discord for the {role.Mention} role, add a reaction to this message!");

            var msg = await channel.SendMessageAsync(embed: embed.Build());
            await msg.AddReactionAsync(new Emoji("👍"));
            await SetSyncMessageAsync(product, msg.Id);
        }

        [Command("setsyncmessage"), Alias("setsyncmsg")]
        [Summary("Specify a premade message to watch for reactions")]
        [Remarks("1st Parameter: The name or id of a linked product" +
                 "2nd Parameter: The id of the message to watch for reactions")]
        public Task SetSyncMessageAsync(RexoProduct product, ulong msgId)
        {
            product.WatchMessageId = msgId;
            _db.Update(product);
            _db.SaveChanges();
            return Task.CompletedTask;
        }
    }
}
