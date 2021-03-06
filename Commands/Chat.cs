﻿using System.Threading.Tasks;
using Discord.Commands;

namespace SteamDiscordBot.Commands
{
    public class ChatCommand : ModuleBase
    {
        [Command("chat"), Summary("Uses a Markov model with to generate response text.")]
        public async Task Say()
        {
            string response = Program.Instance.markov.ReadFromGuild(Context.Guild.Id);
            await Context.Channel.SendMessageAsync(response);
        }
    }

    public class ChatAboutCommand : ModuleBase
    {
        [Command("chat about"), Summary("Uses a Markov model with to generate response text using starting words.")]
        public async Task Say(params string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                args[i] = args[i].ToLower();

            string response = Program.Instance.markov.ReadFromGuild(Context.Guild.Id, args);
            if (response.Length == 0)
            {
                await Context.Channel.SendMessageAsync("I can't :(");
                return;
            }

            await Context.Channel.SendMessageAsync(string.Join(" ", args) + " " + response);
        }
    }

    public class ChatRemoveCommand : ModuleBase
    {
        [Command("chatremove"), Summary("Removes the term from knowledgebase. [Owner only]")]
        public async Task Say(string term)
        {
            if (Context.User.Id != (ulong)Program.config.DiscordAdminId)
            {
                await Context.Channel.SendMessageAsync("Contact "
                                        + Program.config.DiscordAdminContact
                                        + " if you believe terms should be removed.");
                return;
            }

            int amount = await Program.Instance.markov.RemoveFromGuild(Context.Guild.Id, term);
            await Context.Channel.SendMessageAsync(string.Format("Removed \"{0}\" from {1} lines", term, amount));
        }
    }

    public class ChatKnowledgeBaseCommand : ModuleBase
    {
        [Command("chatknowledge"), Summary("Sends a pastebin link containing its knowledgebase.")]
        public async Task Say()
        {
            string knowledgebase = Program.Instance.markov.GetHastebinLink(Context.Guild.Id);
            await Context.Channel.SendMessageAsync("Here's my knowlege base: " + knowledgebase);
        }
    }
}
