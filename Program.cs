﻿using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Discord.Net.Providers.WS4Net;

using Microsoft.Extensions.DependencyInjection;

using SteamDiscordBot.Jobs;
using SteamDiscordBot.Steam;
using SteamDiscordBot.Commands;

using Octokit;
using JsonConfig;

namespace SteamDiscordBot
{
    class Program
    {
        public static readonly string VERSION = "$$version$";

        // STEAM
        public SteamConnection connection;
        public JobManager manager;

        // DISCORD
        public DiscordSocketClient client;
        private CommandService commands;
        private IServiceProvider services;

        // GITHUB
        public GitHubClient ghClient;

        // HANDLERS
        public MarkovHandler markov;
        public FactHandler facts;

        // BOT
        public static Program Instance;
        public static dynamic config;
        public Dictionary<ulong, List<MsgInfo>> messageHist;
        public Dictionary<ulong, string> triggerMap;
        public Random random;

        public static void Main(string[] args)
        {
            try
            {
                var reader = new StreamReader("settings.json");
                config = Config.ApplyJson(reader.ReadToEnd(), new ConfigObject());
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to load configuration file settings.json!\nReason:" + e.Message);
                Environment.Exit(0);
            }

            if (config.DiscordBotToken.Length == 0)
            {
                Console.WriteLine("You must supply a DiscordBotToken!");
                Environment.Exit(0);
            }

            Instance = new Program();
            Instance.MainAsync().GetAwaiter().GetResult();
        }

        private async Task MainAsync()
        {
            var startupStr = string.Format("Bot starting up. ({0} by Michael Flaherty)", Program.VERSION);
            await Log(new LogMessage(LogSeverity.Info, "Startup", startupStr));

            var socketConfig = new DiscordSocketConfig
            {
                WebSocketProvider = WS4NetProvider.Instance,
                LogLevel = LogSeverity.Verbose
            };

            client = new DiscordSocketClient(socketConfig);
            commands = new CommandService();
            services = new ServiceCollection().BuildServiceProvider();

            messageHist = new Dictionary<ulong, List<MsgInfo>>();
            triggerMap = new Dictionary<ulong, string>();
            markov = new MarkovHandler();
            facts = new FactHandler();
            random = new Random();

            client.Log += Log;
            client.GuildAvailable += OnGuildAvailable;

            client.MessageReceived += HandleCommand;
            await commands.AddModulesAsync(Assembly.GetEntryAssembly());

            await client.LoginAsync(TokenType.Bot, config.DiscordBotToken);
            await client.StartAsync();

            // Connect to steam and pump callbacks 
            connection = new SteamConnection(config.SteamUsername, config.SteamPassword);
            connection.Connect();

            ghClient = new GitHubClient(new ProductHeaderValue(Program.config.GitHubUpdateRepository));
            if (config.GitHubAuthToken.Length != 0)
                ghClient.Credentials = new Credentials(config.GitHubAuthToken);

            // Handle Jobs
            manager = new JobManager(config.JobInterval); // time in seconds to run each job
            if (config.SelfUpdateListener && config.GitHubAuthToken.Length != 0)
                manager.AddJob(new SelfUpdateListener());
            if (config.SteamCheckJob)
                manager.AddJob(new SteamCheckJob(connection));
            if (config.AlliedModdersThreadJob)
                manager.AddJob(new AlliedModdersThreadJob("https://forums.alliedmods.net/external.php?newpost=true&forumids=108", "sourcemod"));
                
            foreach (uint appid in config.AppIDList)
            {
                manager.AddJob(new UpdateJob(appid));
            }

            manager.StartJobs();

            await Task.Delay(-1);
        }

        private async Task OnGuildAvailable(SocketGuild arg)
        {
            if (!messageHist.ContainsKey(arg.Id))
                messageHist.Add(arg.Id, new List<MsgInfo>());

            bool found = false;
            if (HasMember(config, "GuildTriggers"))
            {
                foreach (string str in config.GuildTriggers) // we'll loop and find id matches to overwrite the triggerMap entry
                {
                    string[] args = str.Split(':');
                    if (args[0].Equals("" + arg.Id))
                    {
                        found = true;
                        triggerMap.Add(arg.Id, args[1]);
                    }
                }
            }
            if (!found)
                triggerMap.Add(arg.Id, "!"); // default command trigger

            /* This is annoying, but since we have the possibility of parsing
             * huge amounts of text, we need to create a thread for each guild
             * when they join and do all of the text processing there. 
             */
            await markov.AddGuild(arg.Id);
            await facts.AddGuild(arg.Id);
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            var message = messageParam as SocketUserMessage;
            if (message == null) return;
            if (message.Author.IsBot) return;

            var context = new CommandContext(client, message);

            int argPos = 0;
            if (!(message.HasStringPrefix(triggerMap[context.Guild.Id], ref argPos)
                || message.HasMentionPrefix(client.CurrentUser, ref argPos)))
            {
                MsgInfo info = new MsgInfo()
                {
                    message = message.Content,
                    user = message.Author.Id
                };
                messageHist[context.Guild.Id].Add(info);
                markov.WriteToGuild(context.Guild.Id, message.Content);
                return;
            }

            if (IsCommandDisabled(message.Content.Split(' ')[0].Substring(1)))
            {
                await context.Channel.SendMessageAsync("That command is disabled!");
                return;
            }

            var result = await commands.ExecuteAsync(context, argPos, services);
            if (!result.IsSuccess)
                await context.Channel.SendMessageAsync(result.ErrorReason);
        }

        public Task Log(LogMessage msg)
        {
            return Task.Run(() => Console.WriteLine(msg.ToString()));
        }

        public static bool IsCommandDisabled(string cmd)
        {
            foreach (string var in Program.config.DisabledCommands)
            {
                if (var.Equals(cmd))
                {
                    return true;
                }
            }

            return false;
        }

        public static string BuildPath(string file)
        {
            string exe = Assembly.GetEntryAssembly().Location;
            string[] pieces = exe.Split('/');
            string combo = "";

            for (int i = 0; i < pieces.Length - 1; i++)
            {
                combo += pieces + "/";
            }

            return combo + file;
        }

        public static bool HasMember(dynamic obj, string name)
        {
            var it = obj.Keys.GetEnumerator();
            while (it.MoveNext())
            {
                 if (it.Current == name)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
