using LowLevelInput.Hooks;
using Newtonsoft.Json.Linq;
using OrbWalker.modules;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace OrbWalker
{
    public class Program
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        private static int width = Screen.PrimaryScreen.Bounds.Height;
        private const string ActivePlayerEndpoint = @"https://127.0.0.1:2999/liveclientdata/activeplayer";   //对局数据API
        private const string PlayerListEndpoint = @"https://127.0.0.1:2999/liveclientdata/playerlist";
        private const string ChampionStatsEndpoint = @"https://raw.communitydragon.org/latest/game/data/characters/"; //最新版本英雄基础数据查询
        private const string SettingsFile = @"settings\settings.json";
        private const string OnlineCheck = @"http://h553302670.3vkj.net/tongji/online.asp";//查询在线人数软件使用次数版本信息

        private static bool HasProcess = false;
        private static bool IsExiting = false;
        private static bool IsIntializingValues = false;
        private static bool IsUpdatingAttackValues = false;

        private static readonly Settings CurrentSettings = new Settings();
        private static readonly WebClient Client = new WebClient();
        private static readonly InputManager InputManager = new InputManager();
        private static Process LeagueProcess = null;

        private static readonly Timer OrbWalkTimer = new Timer(100d / 3d);

        private static bool OrbWalkerTimerActive = false;
        private static bool KeyMinions = false;
        private static bool KeyEnemies = false;
        private static Point LastMovePoint;

        private static string ActivePlayerName = string.Empty;
        private static string ChampionName = string.Empty;
        private static string RawChampionName = string.Empty;
        private static string online = string.Empty;
        private static string times = string.Empty;
        private static string version = "v12.15";
        private static string Version = string.Empty;

        private static double ClientAttackSpeed = 0.625;
        private static double ChampionAttackCastTime = 0.625;
        private static double ChampionAttackTotalTime = 0.625;
        private static double ChampionAttackSpeedRatio = 0.625;
        private static double ChampionAttackDelayPercent = 0.3;
        private static double ChampionAttackDelayScaling = 1.0;

        /// <summary>
        /// 这是一个缓冲区，可防止因 fps、ping 或其他原因意外取消自动攻击
        /// </summary>
        private static readonly double WindupBuffer = 1d / 15d;

        // 限制最快输入延迟
        private static readonly double MinInputDelay = 1d / 30d;

        // 定时器间隔
        private static readonly double OrderTickRate = 1d / 30d;

#if DEBUG
        private static int TimerCallbackCounter = 0;
#endif

        // 前摇计算 计算方式详见https://leagueoflegends.fandom.com/wiki/Basic_attack#Attack_speed
        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static double GetWindupDuration() => (GetSecondsPerAttack() * ChampionAttackDelayPercent - ChampionAttackCastTime) * ChampionAttackDelayScaling + ChampionAttackCastTime;
        public static double GetBufferedWindupDuration() => GetWindupDuration() + WindupBuffer;

        public static void Main(string[] args)
        {
            if (!File.Exists(SettingsFile))
            {
                Directory.CreateDirectory("settings");
                CurrentSettings.CreateNew(SettingsFile);
            }
            else
            {
                CurrentSettings.Load(SettingsFile);
            }

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            Client.Proxy = null;

            Console.Clear();
            Console.CursorVisible = false;

            InputManager.Initialize();
            InputManager.OnKeyboardEvent += InputManager_OnKeyboardEvent;
            InputManager.OnMouseEvent += InputManager_OnMouseEvent;

            OrbWalkTimer.Elapsed += OrbWalkTimer_Elapsed;
#if DEBUG
            Timer callbackTimer = new Timer(16.66);
            callbackTimer.Elapsed += Timer_CallbackLog;
#endif

            Timer attackSpeedCacheTimer = new Timer(OrderTickRate);
            attackSpeedCacheTimer.Elapsed += AttackSpeedCacheTimer_Elapsed;
            JToken onlineCheckToken = null;
            onlineCheckToken = JToken.Parse(Client.DownloadString(OnlineCheck));
            online = onlineCheckToken?["online"].ToString();
            times = onlineCheckToken?["times"].ToString();
            Version = onlineCheckToken?["version"].ToString();
            Console.Title = $"{version}";
            if (version!= Version)
            {
                Console.WriteLine($"当前版本已更新为{Version}，请下载最新版本使用！\n永久更新地址：https://xmmy.lanzouf.com/b0e7u84pe 密码：52PJ");               
                Console.Write("按任意键退出...");
                Console.ReadKey(true);
                Environment.Exit(0);
            }

            attackSpeedCacheTimer.Start();
            Console.WriteLine($"版本:{version}\n当前在线人数:{online}人\n软件使用次数:{times}次\n");
            Console.WriteLine($"按住{(VirtualKeyCode)CurrentSettings.ActivationKeyMinions}激活走砍清兵");
            Console.WriteLine($"按住{(VirtualKeyCode)CurrentSettings.ActivationKeyEnemies}激活走砍A人\n");
            Console.WriteLine($"对应热键可以在软件目录settings文件夹更改，只需更改键码对应键值");
            CheckLeagueProcess();

            Console.ReadLine();
        }

#if DEBUG
        private static void Timer_CallbackLog(object sender, ElapsedEventArgs e)
        {
            if (TimerCallbackCounter > 1 || TimerCallbackCounter < 0)
            {
                Console.Clear();
                Console.WriteLine("检测到定时器错误");
                throw new Exception("定时器不能同时运行");
            }
        }
#endif

        private static void InputManager_OnMouseEvent(VirtualKeyCode key, KeyState state, int x, int y)
        {
        }

        private static void InputManager_OnKeyboardEvent(VirtualKeyCode key, KeyState state)
        {
            if (key == (VirtualKeyCode)CurrentSettings.ActivationKeyMinions)
            {
                switch (state)
                {
                    case KeyState.Down when !OrbWalkerTimerActive:
                        OrbWalkerTimerActive = true;
                        KeyMinions = true;
                        OrbWalkTimer.Start();
                        break;

                    case KeyState.Up when OrbWalkerTimerActive:
                        OrbWalkerTimerActive = false;
                        KeyMinions = false;
                        OrbWalkTimer.Stop();
                        break;
                }
            }
            if (key == (VirtualKeyCode)CurrentSettings.ActivationKeyEnemies)
            {
                switch (state)
                {
                    case KeyState.Down when !OrbWalkerTimerActive:
                        OrbWalkerTimerActive = true;
                        KeyEnemies = true;
                        OrbWalkTimer.Start();
                        break;

                    case KeyState.Up when OrbWalkerTimerActive:
                        OrbWalkerTimerActive = false;
                        KeyEnemies = false;
                        OrbWalkTimer.Stop();
                        break;
                }
            }
        }

        // 控制实例
        private static DateTime nextInput = default;
        private static DateTime nextMove = default;
        private static DateTime nextAttack = default;

        private static readonly Stopwatch owStopWatch = new Stopwatch();

        private static void OrbWalkTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
#if DEBUG
            owStopWatch.Start();
            TimerCallbackCounter++;
#endif
            if (!HasProcess || IsExiting || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
            {
#if DEBUG
                TimerCallbackCounter--;
#endif

                return;
            }

            // 将计时器开始的时间存储到变量中以提高可读性
            var time = e.SignalTime;

            //这用于在等待准备攻击时控制移动命令
            //如果此函数运行的频率不够高，则不需要这样做
            //如果不是，你可能会导致这个计时器和这个函数的计时器不同步
            //   导致（最坏情况）OrderTickRate + MinInputDelay 延迟
            //目前被禁用
            if (true || nextInput < time)
            {
                Point enemyPos = ChampPosition.GetEnemyPosition(width);
                if (KeyMinions) //清兵走A控制
                {
                    // 如果可以攻击
                    if (nextAttack < time)
                    {
                        // 存储当前时间 + 输入延迟，以便我们知道什么时候可以移动
                        nextInput = time.AddSeconds(MinInputDelay);

                        // 攻击指令
                        InputSimulator.Keyboard.KeyDown((ushort)DirectInputKeys.DIK_A);
                        InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Left);
                        InputSimulator.Keyboard.KeyUp((ushort)DirectInputKeys.DIK_A);

                        //已经发送了攻击指令，需要重新获取时间，因为我不知道输入需要多长时间
                        //我假设它可以忽略不计
                        //如果你考虑保留这个，请检查实际差异是什么
                        var attackTime = DateTime.Now;

                        //存储下一次攻击/移动的时间
                        nextMove = attackTime.AddSeconds(GetBufferedWindupDuration());
                        nextAttack = attackTime.AddSeconds(GetSecondsPerAttack());
                    }
                    //如果不能攻击但可以移动
                    else if (nextMove < time)
                    {
                        //存储当前时间 + 输入延迟，以便知道什么时候可以攻击/下一步移动
                        nextInput = time.AddSeconds(MinInputDelay);

                        //移动指令
                        InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Right);
                    }
                }
                if (KeyEnemies)//只走A英雄控制
                {

                    // 如果可以攻击
                    if (nextAttack < time)
                    {
                        nextInput = time.AddSeconds(MinInputDelay);
                        LastMovePoint = Cursor.Position; //在开始攻击之前先储存当前鼠标坐标
                        KeyMouseHandler.IssueOrder(OrderEnum.AttackUnit, enemyPos); // 鼠标指针指向英雄，因为找色缘故默认左上到右下优先级

                        // 存储当前时间 + 输入延迟，以便我们知道什么时候可以移动

                        var attackTime = DateTime.Now;

                        // 存储下一次攻击/移动的时间
                        nextMove = attackTime.AddSeconds(GetBufferedWindupDuration());
                        nextAttack = attackTime.AddSeconds(GetSecondsPerAttack());
                        //这里就是上面所说的输入延迟，如果没有会导致A不出来
                        System.Threading.Thread.Sleep(10);//延迟时间受fps、ping值影响，这里以100帧为例
                        KeyMouseHandler.IssueOrder(OrderEnum.MoveMouse, LastMovePoint); // 鼠标移回之前坐标，并且不做任何操作
                    }
                    else if (nextMove < time)
                    {
                        // 存储当前时间 + 输入延迟，以便知道什么时候可以攻击/下一步移动
                        nextInput = time.AddSeconds(MinInputDelay);
                        // 移动指令
                        InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Right);
                    }


                }

            }
#if DEBUG
            TimerCallbackCounter--;
            owStopWatch.Reset();
#endif
        }

        private static void CheckLeagueProcess()
        {
            while (LeagueProcess is null || !HasProcess)
            {
                LeagueProcess = Process.GetProcessesByName("League of Legends").FirstOrDefault();
                if (LeagueProcess is null || LeagueProcess.HasExited)
                {
                    continue;
                }
                HasProcess = true;
                LeagueProcess.EnableRaisingEvents = true;
                LeagueProcess.Exited += LeagueProcess_Exited;
            }
        }

        private static void LeagueProcess_Exited(object sender, EventArgs e)
        {
            HasProcess = false;
            LeagueProcess = null;
            //Console.Clear();
            Console.WriteLine("League Process Exited");
            CheckLeagueProcess();
        }

        private static void AttackSpeedCacheTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (HasProcess && !IsExiting && !IsIntializingValues && !IsUpdatingAttackValues)
            {
                IsUpdatingAttackValues = true;

                JToken activePlayerToken = null;
                try
                {
                    activePlayerToken = JToken.Parse(Client.DownloadString(ActivePlayerEndpoint));
                }
                catch
                {
                    IsUpdatingAttackValues = false;
                    return;
                }

                if (string.IsNullOrEmpty(ChampionName))
                {
                    ActivePlayerName = activePlayerToken?["summonerName"].ToString();
                    IsIntializingValues = true;
                    JToken playerListToken = JToken.Parse(Client.DownloadString(PlayerListEndpoint));
                    foreach (JToken token in playerListToken)
                    {
                        if (token["summonerName"].ToString().Equals(ActivePlayerName))
                        {
                            ChampionName = token["championName"].ToString();
                            string[] rawNameArray = token["rawChampionName"].ToString().Split('_', StringSplitOptions.RemoveEmptyEntries);
                            RawChampionName = rawNameArray[^1];
                        }
                    }

                    if (!GetChampionBaseValues(RawChampionName))
                    {
                        IsIntializingValues = false;
                        IsUpdatingAttackValues = false;
                        return;
                    }

#if DEBUG
                    Console.Title = $"({ActivePlayerName}) {ChampionName}";
#endif

                    IsIntializingValues = false;
                }

#if DEBUG
                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"{owStopWatch.ElapsedMilliseconds}\n" +
                    $"Attack Speed Ratio: {ChampionAttackSpeedRatio}\n" +
                    $"Windup Percent: {ChampionAttackDelayPercent}\n" +
                    $"Current AS: {ClientAttackSpeed:0.00####}\n" +
                    $"Seconds Per Attack: {GetSecondsPerAttack():0.00####}\n" +
                    $"Windup Duration: {GetWindupDuration():0.00####}s + {WindupBuffer}s delay\n" +
                    $"Attack Down Time: {GetSecondsPerAttack() - GetWindupDuration():0.00####}s");
#endif

                ClientAttackSpeed = activePlayerToken["championStats"]["attackSpeed"].Value<double>();
                IsUpdatingAttackValues = false;
            }
        }
        //两种不同前摇修正值计算
        private static bool GetChampionBaseValues(string championName)
        {
            string lowerChampionName = championName.ToLower();
            JToken championBinToken = null;
            try
            {
                championBinToken = JToken.Parse(Client.DownloadString($"{ChampionStatsEndpoint}{lowerChampionName}/{lowerChampionName}.bin.json"));
            }
            catch
            {
                return false;
            }
            JToken championRootStats = championBinToken[$"Characters/{championName}/CharacterRecords/Root"];
            ChampionAttackSpeedRatio = championRootStats["attackSpeedRatio"].Value<double>(); ;

            JToken championBasicAttackInfoToken = championRootStats["basicAttack"];
            JToken championAttackDelayOffsetToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercent"];
            JToken championAttackDelayOffsetSpeedRatioToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercentAttackSpeedRatio"];

            if (championAttackDelayOffsetSpeedRatioToken?.Value<double?>() != null)
            {
                ChampionAttackDelayScaling = championAttackDelayOffsetSpeedRatioToken.Value<double>();
            }

            if (championAttackDelayOffsetToken?.Value<double?>() == null)
            {
                JToken attackTotalTimeToken = championBasicAttackInfoToken["mAttackTotalTime"];
                JToken attackCastTimeToken = championBasicAttackInfoToken["mAttackCastTime"];

                if (attackTotalTimeToken?.Value<double?>() == null && attackCastTimeToken?.Value<double?>() == null)
                {
                    string attackName = championBasicAttackInfoToken["mAttackName"].ToString();
                    string attackSpell = $"Characters/{attackName.Split(new[] { "BasicAttack" }, StringSplitOptions.RemoveEmptyEntries)[0]}/Spells/{attackName}";
                    ChampionAttackDelayPercent += championBinToken[attackSpell]["mSpell"]["delayCastOffsetPercent"].Value<double>();
                }
                else
                {
                    ChampionAttackTotalTime = attackTotalTimeToken.Value<double>();
                    ChampionAttackCastTime = attackCastTimeToken.Value<double>(); ;

                    ChampionAttackDelayPercent = ChampionAttackCastTime / ChampionAttackTotalTime;
                }
            }
            else
            {
                ChampionAttackDelayPercent += championAttackDelayOffsetToken.Value<double>(); ;
            }

            return true;
        }
    }
}
