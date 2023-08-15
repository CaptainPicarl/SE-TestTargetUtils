using CoreSystems.Api;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.EntityComponents.Blocks;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.AI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Scripting;
using VRageMath;

#region APIs

#endregion
namespace IngameScript
{
    class Utility : MyGridProgram
    {
        #region PID Class

        /// <summary>
        /// Discrete time PID controller class.
        /// Last edited: 2022/08/11 - Whiplash141
        /// </summary>
        public class PID
        {
            public double Kp { get; set; } = 0;
            public double Ki { get; set; } = 0;
            public double Kd { get; set; } = 0;
            public double Value { get; private set; }

            double _timeStep = 0;
            double _inverseTimeStep = 0;
            double _errorSum = 0;
            double _lastError = 0;
            bool _firstRun = true;

            public PID(double kp, double ki, double kd, double timeStep)
            {
                Kp = kp;
                Ki = ki;
                Kd = kd;
                _timeStep = timeStep;
                _inverseTimeStep = 1 / _timeStep;
            }

            protected virtual double GetIntegral(double currentError, double errorSum, double timeStep)
            {
                return errorSum + currentError * timeStep;
            }

            public double Control(double error)
            {
                //Compute derivative term
                double errorDerivative = (error - _lastError) * _inverseTimeStep;

                if (_firstRun)
                {
                    errorDerivative = 0;
                    _firstRun = false;
                }

                //Get error sum
                _errorSum = GetIntegral(error, _errorSum, _timeStep);

                //Store this error as last error
                _lastError = error;

                //Construct output
                Value = Kp * error + Ki * _errorSum + Kd * errorDerivative;
                return Value;
            }

            public double Control(double error, double timeStep)
            {
                if (timeStep != _timeStep)
                {
                    _timeStep = timeStep;
                    _inverseTimeStep = 1 / _timeStep;
                }
                return Control(error);
            }

            public virtual void Reset()
            {
                _errorSum = 0;
                _lastError = 0;
                _firstRun = true;
            }
        }

        public class DecayingIntegralPID : PID
        {
            public double IntegralDecayRatio { get; set; }

            public DecayingIntegralPID(double kp, double ki, double kd, double timeStep, double decayRatio) : base(kp, ki, kd, timeStep)
            {
                IntegralDecayRatio = decayRatio;
            }

            protected override double GetIntegral(double currentError, double errorSum, double timeStep)
            {
                return errorSum * (1.0 - IntegralDecayRatio) + currentError * timeStep;
            }
        }

        public class ClampedIntegralPID : PID
        {
            public double IntegralUpperBound { get; set; }
            public double IntegralLowerBound { get; set; }

            public ClampedIntegralPID(double kp, double ki, double kd, double timeStep, double lowerBound, double upperBound) : base(kp, ki, kd, timeStep)
            {
                IntegralUpperBound = upperBound;
                IntegralLowerBound = lowerBound;
            }

            protected override double GetIntegral(double currentError, double errorSum, double timeStep)
            {
                errorSum = errorSum + currentError * timeStep;
                return Math.Min(IntegralUpperBound, Math.Max(errorSum, IntegralLowerBound));
            }
        }

        public class BufferedIntegralPID : PID
        {
            readonly Queue<double> _integralBuffer = new Queue<double>();
            public int IntegralBufferSize { get; set; } = 0;

            public BufferedIntegralPID(double kp, double ki, double kd, double timeStep, int bufferSize) : base(kp, ki, kd, timeStep)
            {
                IntegralBufferSize = bufferSize;
            }

            protected override double GetIntegral(double currentError, double errorSum, double timeStep)
            {
                if (_integralBuffer.Count == IntegralBufferSize)
                    _integralBuffer.Dequeue();
                _integralBuffer.Enqueue(currentError * timeStep);
                return _integralBuffer.Sum();
            }

            public override void Reset()
            {
                base.Reset();
                _integralBuffer.Clear();
            }
        }

        #endregion

        // declarations for general use in this class

        // Weaponcore common
        List<IMyTerminalBlock> TURRETS = new List<IMyTerminalBlock>();
        List<MyDefinitionId> TEMP_TUR = new List<MyDefinitionId>();
        List<string> definitionSubIds = new List<string>();

        Dictionary<String, int> TEMP_WC = new Dictionary<String, int>();
        Dictionary<MyDetectedEntityInfo, float> TEMP_WC_MDEI = new Dictionary<MyDetectedEntityInfo, float>();

        public void EnumerateEchoIonHydroThrusters(bool verbose)
        {
            Echo("Printing Ion and Hydrogen Thrusters!\n");
            List<IMyThrust> thrusters = new List<IMyThrust>();
            List<IMyThrust> ionThrusters = new List<IMyThrust>();
            List<IMyThrust> hydroThrusters = new List<IMyThrust>();

            GridTerminalSystem.GetBlocksOfType(thrusters);
            GridTerminalSystem.GetBlocksOfType(ionThrusters);

            if (thrusters.Count == 0)
            {
                if (verbose)
                {
                    Echo("No Thrusters populated in list!\n");
                }
                return;
            }
            else
            {

                foreach(IMyThrust thruster in thrusters)
                {
                    if (verbose)
                    {
                        Echo($"Found Thruster:{thruster.BlockDefinition.SubtypeName}");
                    }

                    if (thruster.BlockDefinition.ToString().ToUpperInvariant().Contains("HYDRO"))
                    {
                        hydroThrusters.Add(thruster);
                    }

                    if (thruster.BlockDefinition.ToString().ToUpperInvariant().Contains("MODULAR"))
                    {
                        ionThrusters.Add(thruster);
                    }
                }
                Echo($"Found {hydroThrusters.Count()} hydro thrusters and {ionThrusters.Count()} ion thrusters!\n");

                foreach (IMyThrust ionThrust in ionThrusters)
                {
                    Echo($"Found Ion Thruster {ionThrust.BlockDefinition.SubtypeId}");
                }

                foreach (IMyThrust hydroThruster in hydroThrusters)
                {
                    Echo($"Found Hydro Thruster {hydroThruster.BlockDefinition.SubtypeId}");
                }

            }

        }

        public void renderIterateArmorStatus(IMyTextSurface screen)
        {
            // WIP
            // TODO: Finish this!
            IMyCubeGrid ShipCubegrid = Me.CubeGrid;
            List<IMySlimBlock> MySlimBlocks = new List<IMySlimBlock>();
            MySprite cellSprite = new MySprite() {Alignment = TextAlignment.CENTER, };

            GridTerminalSystem.GetBlocksOfType<IMySlimBlock>(MySlimBlocks);

            // This just echos the blocks and positions you find
            for(int i = 0; i < MySlimBlocks.Count; i++)
            {
                Echo(MySlimBlocks[i].Position.ToString());
            }
            
        }

        public void printScriptsFromPB()
        {
            List<string> scripts = new List<string>();

            // populates the scripts array with scripts.
            Me.GetSurface(0).GetScripts(scripts);

            Echo("Printing available scripts");

            foreach (string script in scripts)
            {
                Echo(script);
            }
        }

        public void EchoShipWeapons(List<MyDefinitionId> ShipWCWeaponsDefIDs, List<IMyTerminalBlock> ShipWeapons, WcPbApi WCapi)
        {
            #region WCLoadWeapons
            // BEGIN --  Recreated per:
            // https://steamcommunity.com/sharedfiles/filedetails/?id=2178802013
            WCapi.GetAllCoreWeapons(ShipWCWeaponsDefIDs);

            List<string> definitionSubIds = new List<string>();

            ShipWCWeaponsDefIDs.ForEach(d => definitionSubIds.Add(d.SubtypeName));

            // Populate the weapons list.
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(ShipWeapons, block => block.CubeGrid == Me.CubeGrid && definitionSubIds.Contains(block.BlockDefinition.SubtypeName));

            // Note for future self: This is where we get the separate railgun lists from the weapons list above. Well...really, there are two lists.
            Echo("WC Found the following weapons:\n");
            foreach(IMyTerminalBlock weapon in ShipWeapons)
            {
                Echo($"{weapon.BlockDefinition.SubtypeName}");
            }

            // END -- Recreation.
            #endregion
        }

        public void printWeaponcoreWeapons(WcPbApi WCapi)
        {
            List<IMyTerminalBlock> TURRETS = new List<IMyTerminalBlock>();
            List<MyDefinitionId> TEMP_TUR = new List<MyDefinitionId>();
            Dictionary<string, int> TEMP_BWMAP = new Dictionary<string, int>();
            WCapi.GetAllCoreTurrets(TEMP_TUR);

            List<string> definitionSubIds = new List<string>();

            TEMP_TUR.ForEach(d => definitionSubIds.Add(d.SubtypeName));

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(TURRETS, b => b.CubeGrid ==
                Me.CubeGrid && definitionSubIds.Contains(b.BlockDefinition.SubtypeName));

            Echo("Printing Weaponcore Turrets\n");

            foreach (IMyTerminalBlock turret in TURRETS)
            {
                WCapi.GetBlockWeaponMap(turret, TEMP_BWMAP);

                foreach (string turretKey in TEMP_BWMAP.Keys)
                {
                    Echo(turretKey.ToString());
                    Echo(TEMP_BWMAP[turretKey].ToString());
                }
            }
        }

        public void FireOnceAllWeaponcoreTurrets(WcPbApi api)
        {
            List<IMyTerminalBlock> TURRETS = new List<IMyTerminalBlock>();
            List<MyDefinitionId> TEMP_TUR = new List<MyDefinitionId>();
            List<string> definitionSubIds = new List<string>();

            api.GetAllCoreTurrets(TEMP_TUR);

            TEMP_TUR.ForEach(d => definitionSubIds.Add(d.SubtypeName));

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(TURRETS, b => b.CubeGrid ==
                Me.CubeGrid && definitionSubIds.Contains(b.BlockDefinition.SubtypeName));

            Echo("Firing Weaponcore Turrets\n");

            foreach (IMyTerminalBlock turret in TURRETS)
            {
                api.ToggleWeaponFire(turret, true, true, 0);
            }
        }

        public void EchoTerminalProperties(IMyTerminalBlock block)
        {
            List<ITerminalProperty> properties = new List<ITerminalProperty>();
            block.GetProperties(properties);
            foreach (var property in properties)
            {
                Echo(property.Id);
            }
        }

        public void printAvailableScripts(IMyProgrammableBlock pb)
        {
            List<string> scripts = new List<string>();

            pb.GetSurface(0).GetScripts(scripts);

            Echo("Printing available scripts");

            foreach (string script in scripts)
            {
                Echo(script);
            }
        }


        public void printWCSortedThreats(WcPbApi api, IMyTerminalBlock pBlock)
        {
            api.GetAllCoreTurrets(TEMP_TUR);

            List<string> definitionSubIds = new List<string>();

            TEMP_TUR.ForEach(d => definitionSubIds.Add(d.SubtypeName));

            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(TURRETS, b => b.CubeGrid ==
                Me.CubeGrid && definitionSubIds.Contains(b.BlockDefinition.SubtypeName));

            foreach (IMyTerminalBlock turret in TURRETS)
            {
                api.GetBlockWeaponMap(turret, TEMP_WC);
            }

            api.GetSortedThreats(pBlock, TEMP_WC_MDEI);

            Echo("Printing Sorted Threats.\n");

            foreach (MyDetectedEntityInfo contact in TEMP_WC_MDEI.Keys)
            {
                Echo(contact.Name.ToString());
                Echo(contact.Position.ToString());
                Echo(contact.Velocity.ToString());
            }
        }
    }
}
