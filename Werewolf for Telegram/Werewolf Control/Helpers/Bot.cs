﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Database;
using Microsoft.Win32;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Werewolf_Control.Handler;
using Werewolf_Control.Models;

namespace Werewolf_Control.Helpers
{
    internal static class Bot
    {
        internal static string TelegramAPIKey;
        public static HashSet<Node> Nodes = new HashSet<Node>();
        public static Client Api;

        public static User Me;
        public static DateTime StartTime = DateTime.UtcNow;
        public static bool Running = true;
        public static long CommandsReceived = 0;
        public static long MessagesReceived = 0;
        public static long TotalPlayers = 0;
        public static long TotalGames = 0;
        public static Random R = new Random();
        public static XDocument English;
        public static int MessagesSent = 0;
        internal static string RootDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }
        internal delegate void ChatCommandMethod(Update u, string[] args);
        internal static List<Command> Commands = new List<Command>();
        internal static string LanguageDirectory => Path.GetFullPath(Path.Combine(RootDirectory, @"..\Languages"));
        internal static string TempLanguageDirectory => Path.GetFullPath(Path.Combine(RootDirectory, @"..\TempLanguageFiles"));
        public static void Initialize(string updateid = null)
        {

            //get api token from registry
            var key =
                    RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                        .OpenSubKey("SOFTWARE\\Werewolf");
#if DEBUG
            TelegramAPIKey = key.GetValue("DebugAPI").ToString();
#elif RELEASE
            TelegramAPIKey = key.GetValue("ProductionAPI").ToString();
#elif RELEASE2
            TelegramAPIKey = key.GetValue("ProductionAPI2").ToString();
#elif BETA
            TelegramAPIKey = key.GetValue("BetaAPI").ToString();
#endif
            Api = new Client(TelegramAPIKey);

            English = XDocument.Load(Path.Combine(LanguageDirectory, "English.xml"));

            //load the commands list
            foreach (var m in typeof(Commands).GetMethods())
            {
                var c = new Command();
                foreach (var a in m.GetCustomAttributes(true))
                {
                    if (a is Attributes.Command)
                    {
                        var ca = a as Attributes.Command;
                        c.Blockable = ca.Blockable;
                        c.DevOnly = ca.DevOnly;
                        c.GlobalAdminOnly = ca.GlobalAdminOnly;
                        c.GroupAdminOnly = ca.GroupAdminOnly;
                        c.Trigger = ca.Trigger;
                        c.Method = (ChatCommandMethod)Delegate.CreateDelegate(typeof(ChatCommandMethod), m);
                        c.InGroupOnly = ca.InGroupOnly;
                        Commands.Add(c);
                    }
                }
            }


            Api.UpdateReceived += UpdateHandler.UpdateReceived;
            Api.CallbackQueryReceived += UpdateHandler.CallbackReceived;
            Api.ReceiveError += ApiOnReceiveError;
            Me = Api.GetMe().Result;

            Console.Title += " " + Me.Username;
            if (!String.IsNullOrEmpty(updateid))
                Api.SendTextMessage(updateid, "Control updated\n" + Program.GetVersion());
            StartTime = DateTime.UtcNow;
            //now we can start receiving
            Api.StartReceiving();
        }
        

        private static void ApiOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs)
        {
            if (!Api.IsReceiving)
            {
                Api.StartReceiving();
            }
            var e = receiveErrorEventArgs.ApiRequestException;
            using (var sw = new StreamWriter("apireceiveerror.log", true))
            {
                sw.WriteLine($"{DateTime.Now} {e.ErrorCode} - {e.Message}\n{e.Source}");
            }
                
        }

        private static void Reboot()
        {
            Running = false;
            Program.Running = false;
            Process.Start(Assembly.GetExecutingAssembly().Location);
            Environment.Exit(4);

        }

        //TODO this needs to be an event
        public static void NodeConnected(Node n)
        {
#if DEBUG
            //Api.SendTextMessage(Settings.MainChatId, $"Node connected with guid {n.ClientId}");
#endif
        }

        //TODO this needs to be an event as well
        public static void Disconnect(this Node n, bool notify = true)
        {
#if DEBUG
            //Api.SendTextMessage(Settings.MainChatId, $"Node disconnected with guid {n.ClientId}");
#endif
            if (notify && n.Games.Count > 2)
                foreach (var g in n.Games)
                {
                    Send(UpdateHandler.GetLocaleString("NodeShutsDown", g.Language), g.GroupId);
                }
            Nodes.Remove(n);
            n = null;
        }

        /// <summary>
        /// Gets the node with the least number of current games
        /// </summary>
        /// <returns>Best node, or null if no nodes</returns>
        public static Node GetBestAvailableNode()
        {
            //make sure we remove bad nodes first
            foreach (var n in Nodes.Where(x => x.TcpClient.Connected == false).ToList())
                Nodes.Remove(n);
            return Nodes.Where(x => x.ShuttingDown == false && x.CurrentGames < Settings.MaxGamesPerNode).OrderBy(x => x.CurrentGames).FirstOrDefault(); //if this is null, there are no nodes
        }


        internal static async Task<Message> Send(string message, long id, bool clearKeyboard = false, InlineKeyboardMarkup customMenu = null, ParseMode parseMode = ParseMode.Html)
        {
            MessagesSent++;
            //message = message.Replace("`",@"\`");
            if (clearKeyboard)
            {
                var menu = new ReplyKeyboardHide { HideKeyboard = true };
                return await Api.SendTextMessage(id, message, replyMarkup: menu, disableWebPagePreview: true, parseMode: parseMode);
            }
            else if (customMenu != null)
            {
                return await Api.SendTextMessage(id, message, replyMarkup: customMenu, disableWebPagePreview: true, parseMode: parseMode);
            }
            else
            {
                return await Api.SendTextMessage(id, message, disableWebPagePreview: true, parseMode: parseMode);
            }

        }

        internal static GameInfo GetGroupNodeAndGame(long id)
        {
            var node = Nodes.ToList().FirstOrDefault(n => n.Games.Any(g => g.GroupId == id))?.Games.FirstOrDefault(x => x.GroupId == id);
            if (node == null)
                node = Nodes.ToList().FirstOrDefault(n => n.Games.Any(g => g.GroupId == id))?.Games.FirstOrDefault(x => x.GroupId == id);
            if (node == null)
                node = Nodes.ToList().FirstOrDefault(n => n.Games.Any(g => g.GroupId == id))?.Games.FirstOrDefault(x => x.GroupId == id);
            return node;
        }
    }
}
