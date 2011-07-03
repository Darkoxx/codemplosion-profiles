/*
 * This plugin includes following features:
 * - Detects when stuck and performs unstuck routine
 * - Restarts bot when stuck or at intervals (stability fix)
 * - Fixes stuck in air being attacked for ArchaeologyBuddy
 * - Fixes stuck in shallow water for PoolFisher bot
 *
 * Author: lofi
 */

using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.LootFrame;
using Styx.Logic.POI;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic;
using Styx.Plugins.PluginClass;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals;
using Styx;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using System;
using System.Media;
using Styx.Plugins;

namespace StuckHelper
{
   
    #region form

    public class SettingsForm : Form
    {
        private FlowLayoutPanel panel = new FlowLayoutPanel();
        private Label LabelStuckRoutine = new Label();
        private Label LabelStuckRestart = new Label();
        private Label LabelMountStuck = new Label();
	 private Label LabelStuckRange = new Label();
       
        private TextBox TextStuckRoutine = new TextBox();
        private TextBox TextStuckRestart = new TextBox();
        private TextBox TextMountStuck = new TextBox();
        private TextBox TextStuckRange = new TextBox();
       
        public SettingsForm()
        {
            LabelStuckRoutine.Text = "StuckRoutine:";
            LabelStuckRestart.Text = "StuckRestart:";
            LabelMountStuck.Text = "MountStuck:";
	    LabelStuckRange.Text = "StuckRange:";
	   	    

            TextStuckRoutine.Text = StuckHelper.settings.StuckRoutine.ToString();
            TextStuckRoutine.Width = 190;
            TextStuckRestart.Text = StuckHelper.settings.StuckRestart.ToString();
            TextStuckRestart.Width = 190;
            TextMountStuck.Text = StuckHelper.settings.MountStuck.ToString();
            TextMountStuck.Width = 190;
	    TextStuckRange.Text = StuckHelper.settings.StuckRange.ToString();
            TextStuckRange.Width = 190;
           
            panel.Dock = DockStyle.Fill;
            panel.Controls.Add(LabelStuckRoutine);
            panel.Controls.Add(TextStuckRoutine);
            panel.Controls.Add(LabelStuckRestart);
            panel.Controls.Add(TextStuckRestart);
            panel.Controls.Add(LabelMountStuck);
            panel.Controls.Add(TextMountStuck);
	    panel.Controls.Add(LabelStuckRange);
            panel.Controls.Add(TextStuckRange);
          
            this.Text = "Settings";
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Height = 190;
            this.Width = 320;
            this.Controls.Add(panel);
        }

        protected override void Dispose(bool disposing)
        {
            StuckHelper.settings.StuckRoutine = int.Parse(TextStuckRoutine.Text);
            StuckHelper.settings.StuckRestart = int.Parse(TextStuckRestart.Text);
            StuckHelper.settings.MountStuck = int.Parse(TextMountStuck.Text);
            StuckHelper.settings.StuckRange = int.Parse(TextStuckRange.Text);
           
            StuckHelper.settings.Save();
            base.Dispose(disposing);
        }
    }

    #endregion

    #region settings

    class  StuckHelperSettings : Settings
    {
        public  StuckHelperSettings() : base(Logging.ApplicationPath + "\\Settings\\StuckHelper_" + StyxWoW.Me.Name + ".xml") 
        {
        Load();
        }

        [Setting, DefaultValue(60)]
        public int StuckRoutine { get; set; }

        [Setting, DefaultValue(90)]
        public int StuckRestart { get; set; }

        [Setting, DefaultValue(20)]
        public int MountStuck { get; set; }

	[Setting, DefaultValue(20)]
        public int StuckRange { get; set; }


       }

    #endregion


    class StuckHelper : HBPlugin, IDisposable
    {
        // User configurations
        private static double RestartInterval = 0; // Restart bot ever x mins, 0 to disable
        private static int StopStartTime = 30; // Seconds to wait between stop and start during re-start
       // private static double StuckRange = 10; // Reset standing still timer after moving this distance
       // private static double StuckMins = 1; // Restart bot after standing still this long, 0 to disable
       // private static double StuckRoutineMins = 1; // Perform unstuck after standing still this long, 0 to disable
        private static double ArchBuddyFixMins = 0; // Dismounts after flying still and being attacked, 0 to disable
        private static double PoolFisherFixMins = 0; // Perform unstuck after swimming still this long, 0 to disable
       // private static double MountFixMins = 20; // Perform dismount after mounted still this long, 0 to disable
	
	public override bool WantButton { get { return true; } }
        public override string Name { get { return "- Custom - StuckHelper Ground"; } }
        public override string Author { get { return "bcrazy"; } }
        public override Version Version { get { return new Version(1, 0, 1); } }
         private static LocalPlayer Me { get { return ObjectManager.Me; } }
        private static Stopwatch spamD = new Stopwatch();
        private static Stopwatch TimerRestartFermo = new Stopwatch();
        private static Stopwatch TimerUnstuck = new Stopwatch();
        private static Stopwatch archBuddyTimer = new Stopwatch();
        private static Stopwatch swimTimer = new Stopwatch();
        private static Stopwatch TimerMount = new Stopwatch();
        private static Thread restartIntervalThread = null;
        private static Thread restartWhenStuckThread = null;
        private static WoWPoint lastPoint = new WoWPoint();
        private static Random random = new Random();
 	private static Stopwatch CountRestart = new Stopwatch();
	public static  StuckHelperSettings settings = new  StuckHelperSettings();


	

	private static void PlaySound(string soundFile)
        {
        new SoundPlayer(Path.Combine(Logging.ApplicationPath, @"Plugins\StuckHelper\Sounds\") + soundFile).Play();
        }

	 
	public override string ButtonText
        {
            get
            {
                return "StuckHelper Ground";
            }
        }

	public override void OnButtonPress()
        {
            new SettingsForm().Show();
        }


        public override void Pulse()
          {
            try
            {
		if (Me == null || !ObjectManager.IsInGame || Battlegrounds.IsInsideBattleground)
		{
		TimerRestartFermo.Reset();
                TimerUnstuck.Reset();
		return;
           	}
	
		if (Me.Combat || Me.IsOnTransport)
		{
		TimerRestartFermo.Reset();
                TimerUnstuck.Reset();
		}	


               	 
		
		if (spamD.Elapsed.TotalSeconds < 5.0 && !Me.Combat)
                    return; // spam protection
                spamD.Reset();
                spamD.Start();

                // update game objects
                ObjectManager.Update();  
		Stuck();

	      }
		

            
            catch (Exception e)
            {
                Log("ERROR: " + e.Message + ". See debug log.");
                Logging.WriteDebug("StuckHelper exception:");
                Logging.WriteException(e);
            }
        }

        public override void Initialize()
        {
            if (restartIntervalThread != null && restartIntervalThread.IsAlive)
            {
                restartIntervalThread.Abort();
            }

            if (RestartInterval > 0)
            {
                restartIntervalThread = new Thread(new ThreadStart(RestartIntervalThread));
                restartIntervalThread.Start();
            }

            spamD.Start();

            Log("Loaded version " + Version);
        }

        
        public static void RestartIntervalThread()
        {
            while (true) // wait for Abort()
            {
                Log("Re-starting HB in " + RestartInterval + " minutes...");
                Thread.Sleep((int)(RestartInterval * 60 * 1000));
                RestartBot();
            }
        }

        public static void RestartWhenStuckThread()
        {
            Log("Detected stuck!! Re-starting bot.");

            if (restartIntervalThread != null && restartIntervalThread.IsAlive)
            {
                restartIntervalThread.Abort();
            }
		 
		if (!Me.Combat)
           	{ 
		RestartBot();
		}
           
            if (RestartInterval > 0)
            {
                restartIntervalThread = new Thread(new ThreadStart(RestartIntervalThread));
                restartIntervalThread.Start();
            }
        }

        private static void UnstuckRoutine()
        {
	    PlaySound("Stuck.wav");
            Log("Performing unstuck routine!");
            Lua.DoString("Dismount() CancelShapeshiftForm()");
	                                 

            // swim jumps
            int numJumps = random.Next(2, 4);
            for (int i = 0; i < numJumps; i++)
            {
		Lua.DoString("Dismount() CancelShapeshiftForm()");
                Styx.Helpers.KeyboardManager.PressKey((char)Keys.Space);
                Thread.Sleep(random.Next(2000, 3000));
                Styx.Helpers.KeyboardManager.ReleaseKey((char)Keys.Space);
                Thread.Sleep(random.Next(250, 750));
            }

            // long jumps
            numJumps = random.Next(1, 3);
            for (int i = 0; i < numJumps; i++)
            {
    

            }

            // short jumps
            numJumps = random.Next(1, 3);
            for (int i = 0; i < numJumps; i++)
            {
		Lua.DoString("Dismount() CancelShapeshiftForm()");
                Styx.Helpers.KeyboardManager.PressKey((char)Keys.Space);
                Thread.Sleep(random.Next(30, 50));
                Styx.Helpers.KeyboardManager.PressKey((char)Keys.Down);
                Thread.Sleep(random.Next(500, 750));
                Styx.Helpers.KeyboardManager.ReleaseKey((char)Keys.Down);
                Styx.Helpers.KeyboardManager.ReleaseKey((char)Keys.Space);
                Thread.Sleep(random.Next(250, 750));
        	Styx.Helpers.KeyboardManager.PressKey((char)Keys.Left);
                Thread.Sleep(random.Next(30, 50));
                Styx.Helpers.KeyboardManager.PressKey((char)Keys.Down);
                Thread.Sleep(random.Next(500, 750));
		Styx.Helpers.KeyboardManager.ReleaseKey((char)Keys.Left);
                Styx.Helpers.KeyboardManager.ReleaseKey((char)Keys.Down);
                Thread.Sleep(random.Next(250, 750)); 
		Styx.Helpers.KeyboardManager.PressKey((char)Keys.Right);
                Thread.Sleep(random.Next(30, 50));
                Styx.Helpers.KeyboardManager.PressKey((char)Keys.Down);
                Thread.Sleep(random.Next(500, 750));
		Styx.Helpers.KeyboardManager.ReleaseKey((char)Keys.Right);
                Styx.Helpers.KeyboardManager.ReleaseKey((char)Keys.Down);
                Thread.Sleep(random.Next(250, 750));       
	}

            lastPoint = new WoWPoint(Me.X, Me.Y, Me.Z);
        }

     private static void RestartBot()
     {
	
        
            if (Me.IsInInstance || Battlegrounds.IsInsideBattleground || Me.Combat)
            {
                Log("Inside an instance or a BG! Skipped HB re-start.");
            }
            else                              
            { 
               
		Log("Re-starting HB...");
		Lua.DoString("Dismount() CancelShapeshiftForm()");
		Lua.DoString("VehicleExit()");
                                       
	        spamD.Reset();
               	TimerUnstuck.Reset();
                TimerMount.Reset();
		TimerRestartFermo.Reset();
               
		
               	 // Lua.DoString("Logout()");
	    	TimerRestartFermo.Reset();
                spamD.Reset();
                TimerUnstuck.Reset();
               	archBuddyTimer.Reset();
                swimTimer.Reset();
                TimerMount.Reset();
		
		TreeRoot.Stop();
               
	                         
               Log("Waiting " + StopStartTime + " seconds...");
               Thread.Sleep(StopStartTime * 1000);

 	    
		Log("Starting HB...");
		 
               // Styx.Logic.BehaviorTree.TreeRoot.Start();
		TreeRoot.Start();
            }
     }
  
 
        private static bool Stuck()
        {
            WoWPoint myPoint = new WoWPoint(Me.X, Me.Y, Me.Z);

            	if (TreeRoot.StatusText.Contains("Wait"))
		{
		TimerRestartFermo.Reset();
                TimerUnstuck.Reset();
		return false;
		}
		//if (TreeRoot.GoalText.Contains("Wait"))
		//{
		//restartStandingStillTimer.Reset();
                //TimerUnstuck.Reset();
		//return false;
		//}

		
		if (TreeRoot.StatusText.Contains("Downloading"))
		{
		TimerRestartFermo.Reset();
                TimerUnstuck.Reset();
		return false;
		}
		
		if (TreeRoot.StatusText.Contains("Loading Tile"))
		{
		TimerRestartFermo.Reset();
                TimerUnstuck.Reset();
		return false;
		}



	    if (myPoint.Distance(lastPoint) > settings.StuckRange)
            {
                lastPoint = myPoint;
               TimerRestartFermo.Reset();
                TimerUnstuck.Reset();
                return false;
            }

	    
            if (!TimerRestartFermo.IsRunning)
               TimerRestartFermo.Start();

            if (!TimerUnstuck.IsRunning)
                TimerUnstuck.Start();

            if (!archBuddyTimer.IsRunning && Me.Combat)
                archBuddyTimer.Start();
            else if (archBuddyTimer.IsRunning && (!Me.Combat))
                archBuddyTimer.Reset();

            if (!swimTimer.IsRunning && Me.IsSwimming)
                swimTimer.Start();
            else if (swimTimer.IsRunning && !Me.IsSwimming)
                swimTimer.Reset();

            if (!TimerMount.IsRunning && Me.Mounted)
             	{    
	    	TimerMount.Start();
		}
            else if (TimerMount.IsRunning && (!Me.Mounted))
               	{
                 TimerMount.Reset();
		}
            if (TimerRestartFermo.Elapsed.TotalSeconds > settings.StuckRestart)
            {
               TimerRestartFermo.Reset();

                if (restartIntervalThread != null && restartIntervalThread.IsAlive)
                {
                    restartIntervalThread.Abort();
                }
                if (restartWhenStuckThread != null && restartWhenStuckThread.IsAlive)
                {
                    restartWhenStuckThread.Abort();
                }

                restartWhenStuckThread = new Thread(new ThreadStart(RestartWhenStuckThread));
                restartWhenStuckThread.Start();
                return true;
            }

            if (TimerUnstuck.Elapsed.TotalSeconds > settings.StuckRoutine && !Me.Combat)
            {
                swimTimer.Reset();
                TimerUnstuck.Reset();
                UnstuckRoutine();
                return true;
            }

            if (PoolFisherFixMins != 0 && swimTimer.Elapsed.TotalMinutes > PoolFisherFixMins)
            {
                swimTimer.Reset();
                TimerUnstuck.Reset();
                UnstuckRoutine();
                return true;
            }

            if (ArchBuddyFixMins != 0 && archBuddyTimer.Elapsed.TotalMinutes > ArchBuddyFixMins)
            {
                Log("Dismounting while flying");
                archBuddyTimer.Reset();
                Mount.Dismount();
                return true;
            }

	if (TimerMount.Elapsed.TotalSeconds > settings.MountStuck && Me.Mounted )
            {
                Log("Dismounting to unstuck");
                TimerMount.Reset();
                //Mount.Dismount();
		Lua.DoString("Dismount() CancelShapeshiftForm()");
                return true;
            }
	 
        return false;
        }


    #region plugin

        private PluginContainer GetPlugin() {
            foreach (PluginContainer p in PluginManager.Plugins)
            {
                if (p != null && p.Name == Name)
                {
                    return p;
                }
            }
            return null;
        }

        private bool IsPluginEnabled()
        {
            PluginContainer p = GetPlugin();
            if (p == null)
            {
                return false;
            }
            return p.Enabled;
        }

        #endregion

        private static void Log(string format, params object[] args)
        {
            Logging.Write(Color.DarkRed, "[StuckHelperGround] " + format, args);
        }
    }
}

