using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using System.Runtime.CompilerServices;
using VRage.Game.ObjectBuilders.VisualScripting;
using VRageRender;
using CoreSystems.Api;
using DefenseShields;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        // 
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.
        static string DefaultEntityName = "NO TARGET DETECTED!";
        static int DSShieldScalar = 10000;

        int MsRetainDamageAccumulation = 10000;
        int MsRetainDamageInstance = 5;
        int CurrCycle = 0;

        WcPbApi WCapi;
        PbApiWrapper DSapi;

        IMyBlockGroup TextSurfaceProvGroup;

        List<IMyTextSurface> TextSurfaceBlocks = new List<IMyTextSurface>();

        string ScreenGroupName = "TTUGeneralInfo";
        string ClosestEntityName = "NO ENTITY DETECTED!";

        float DSShieldHP = 0;
        float DSLastShieldHP = 0;
        float DSShieldPercentage = 0;
        float DSLastShieldPercentage = 0;
        float DsShieldHeat = 0;
        float DSMaxHpCap = 0;
        float DSPowerCap = 0;
        float DSPowerUsed = 0;
        float DSShieldMissing = 0;
        float DSShieldChargeRate = 0;
        float DamageInstance = 0;
        float DamageAccum = 0;
        float HighestDamageInstance = 0;
        float HighestDamageAccum = 0;
        float DamagePerMs;
        float DamagePerSecond;
        float DamageLastInstance;

        TimeSpan msPassed;

        double secondsPassed = 0;

        double msAccum;
        double secondsAccum;
        int RenderDamageScalar = 1;

        Dictionary<MyDetectedEntityInfo, float> WCThreatDict = new Dictionary<MyDetectedEntityInfo, float>();
        MyDetectedEntityInfo? ClosestTargetEntity;
        Vector3D ClosestTargetVector = Vector3D.Zero;

        // We use null to represent 'empty' in this house.
        // don't step in black holes, dork.

        public Program()
        {

            // Prevent silly users from making silly mistakes
            if (MsRetainDamageAccumulation < MsRetainDamageInstance)
            {
                throw new Exception("CyclesRetainDamage cannot be greater than CyclesRetainInstance!");
            }

            try
            {
                WCapi = new WcPbApi();
                WCapi.Activate(Me);
            }
            catch (Exception ex)
            {
                Echo("Weaponcore init failed!");
                Me.CustomData += ("Weaponcore init failed!");
            }


            try
            {
                DSapi = new PbApiWrapper(Me);
            }
            catch (Exception ex)
            {
                Echo("DefenseShield init failed!");
                Me.CustomData += "DefenseShield init failed!";
            }

            TextSurfaceProvGroup = GridTerminalSystem.GetBlockGroupWithName(ScreenGroupName);

            if (TextSurfaceProvGroup == null || TextSurfaceBlocks == null)
            {
                Echo("Setup failure!\nUnable to find basic information screens!\n");
                Me.CustomData += "Setup failure!\nUnable to find basic information screens!\n";
                throw new InvalidOperationException();
            }
            else
            {
                TextSurfaceProvGroup.GetBlocksOfType(TextSurfaceBlocks);
            }


            // Set UpdateFreq
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Shield stuff Init
            DSShieldPercentage = DSapi.GetShieldPercent();
            DSMaxHpCap = DSapi.GetMaxHpCap() * DSShieldScalar;

            DSPowerCap = DSapi.GetPowerCap();
            DSPowerUsed = DSapi.GetPowerUsed();
            DsShieldHeat = DSapi.GetShieldHeat();
            DSShieldChargeRate = DSapi.GetChargeRate();
            DSShieldHP = DSapi.GetCharge() * 100;
            DSShieldMissing = DSMaxHpCap - DSShieldHP;

            secondsPassed = 0;
            msPassed = TimeSpan.Zero;
            msAccum = 0;

            // Lets try and guesstimate a DPS
            DamageInstance = 0;
            DamageAccum = 0;
            DamagePerMs = 0;
        }

        public void Save()
        {

        }

        public void Main(string argument, UpdateType updateSource)
        {
            // Shield stuff
            DSShieldPercentage = DSapi.GetShieldPercent();
            DSMaxHpCap = DSapi.GetMaxHpCap() * DSShieldScalar;

            DSPowerCap = DSapi.GetPowerCap();
            DSPowerUsed = DSapi.GetPowerUsed();
            DsShieldHeat = DSapi.GetShieldHeat();
            DSShieldChargeRate = DSapi.GetChargeRate();
            DSShieldHP = DSapi.GetCharge() * 100;
            DSShieldMissing = DSMaxHpCap - DSShieldHP;

            //Timing stuff. Hopefully usable by some kind of DPS monitor.
            secondsPassed = Runtime.TimeSinceLastRun.TotalSeconds;
            secondsAccum += secondsPassed;

            msPassed = Runtime.TimeSinceLastRun;
            msAccum += msPassed.Milliseconds;

            // Lets try and guesstimate a DPS
            if (DSShieldHP < DSLastShieldHP)
            {
                DamageInstance = DSLastShieldHP - DSShieldHP;

                if (DamageInstance < 0)
                {
                    // Sometimes, on instantiation, DamageInstance will equal less than zero.
                    DamageInstance = 0;
                }

                DamageAccum += DamageInstance;
                DamagePerMs = DamageAccum / (float)msAccum;
                DamagePerSecond = DamageAccum / (float)secondsAccum;


            }

            if (DamageAccum > HighestDamageAccum)
            {
                HighestDamageAccum = DamageAccum;
            }

            WCapi.GetSortedThreats(Me, WCThreatDict);

            //ClosestTargetVector = WCThreatDict.FirstOrDefault(threat => 
            //(Me.GetPosition() - threat.Key.Position).X < (Me.GetPosition() - ClosestTargetVector).X &&
            //(Me.GetPosition() - threat.Key.Position).Y < (Me.GetPosition() - ClosestTargetVector).Y &&
            //(Me.GetPosition() - threat.Key.Position).Z < (Me.GetPosition() - ClosestTargetVector).Z).Key.Position;

            // Trying to use a simply v-w here to determine the closest target
            ClosestTargetVector = WCThreatDict.FirstOrDefault(threat => (Me.GetPosition() - threat.Key.Position).Length() < (Me.GetPosition() - ClosestTargetVector).Length()).Key.Position;

            ClosestTargetEntity = WCThreatDict.FirstOrDefault(threat => (Me.GetPosition() - threat.Key.Position).Length() < (Me.GetPosition() - ClosestTargetVector).Length()).Key;

            // Once we have our closestTarget - we can do stuff like...
            if (ClosestTargetVector != null && ClosestTargetEntity != null)
            {
                // Do stuff with the closest target
                if (ClosestTargetEntity.HasValue)
                {
                    ClosestEntityName = ClosestTargetEntity.Value.Name;
                }
                else
                {
                    ClosestEntityName = DefaultEntityName;
                }
            }

            foreach (IMyTextSurface surface in TextSurfaceBlocks)
            {
                try
                {
                    surface.BackgroundColor = Color.Black;
                    surface.FontColor = Color.Green;
                    surface.FontSize = 1;
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;

                    surface.WriteText($"Closest Target Name: {ClosestEntityName}\n", false);
                    surface.WriteText($"Closest Target Position: {ClosestTargetVector}\n", true);
                    surface.WriteText($"Shield Percentage: {DSShieldPercentage}\n", true);
                    surface.WriteText($"Shield Charge Rate: {DSShieldChargeRate}\n", true);
                    surface.WriteText($"Shield HP: {(int)DSShieldHP}\n", true);
                    surface.WriteText($"Shield missing: {(int)DSShieldMissing}\n", true);
                    surface.WriteText($"Shield MAX: {(int)DSMaxHpCap}\n", true);
                    surface.WriteText($"Shield Heat: {DsShieldHeat}\n", true);
                    //surface.WriteText($"Shield Power Cap: {DSPowerCap}[MW?]\n", true); <-- This always returns bullshit for some reason.
                    surface.WriteText($"Shield Power Used: {DSPowerUsed} [MW?]\n", true);
                    surface.WriteText($"Damage at this Instance: {DamageInstance}\n", true);
                    surface.WriteText($"Damage Per Second: {DamagePerSecond} \n", true);
                    surface.WriteText($"Damage Accumulated: {DamageAccum} \n", true);
                    // surface.WriteText($"Shield Ratio Damage/MilliSecond : {(DamagePerMilliSecondRatio)}\n", true); <- No clue what this even calculates, lel, I just wanted insight.
                    surface.WriteText($"Highest Damage Instance   : {(HighestDamageInstance)}\n", true);
                    surface.WriteText($"Highest Damage Accumulated   : {(HighestDamageAccum)}\n", true);



                }
                catch
                {
                    surface.WriteText("ERROR!");
                }



            }

            // Do 'ms-based' stuff here (Just my term for each call of Main() )
            if (msAccum >= MsRetainDamageInstance)
            {
                DSShieldMissing = 0;

                DamageInstance = 0;
            }

            if (msAccum >= MsRetainDamageAccumulation)
            {
                DamagePerSecond = 0;
                DamagePerMs = 0;
                DamageAccum = 0;
                // we reset the msAccum because we are done
                msAccum = 0;
            }

            // After we have used the ShieldPercent value for whatever shenanigans we want - go ahead and store that value in the 'lastShield' var
            DSLastShieldPercentage = DSShieldPercentage;
            DSLastShieldHP = DSShieldHP;
        }


    }
}
