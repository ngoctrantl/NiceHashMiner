﻿using System;
using NiceHashMiner.Configs;
using NiceHashMiner.Configs.Data;
using NiceHashMiner.Miners.Grouping;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NiceHashMiner.Algorithms;
using NiceHashMinerLegacy.Common.Enums;
using NiceHashMinerLegacy.Common.Device;
using NHM.UUID;
using NHM.DeviceMonitoring;

namespace NiceHashMiner.Devices
{
    public class ComputeDevice
    {
        // migrate ComputeDevice to BaseDevice
        public BaseDevice BaseDevice { get; private set; }

        public int ID => BaseDevice?.ID ?? -1;
        // CPU, NVIDIA, AMD
        public DeviceType DeviceType => BaseDevice?.DeviceType ?? (DeviceType)(-1);
        // UUID now used for saving
        public string Uuid => BaseDevice?.UUID ?? "-1";
        // to identify equality;
        public string Name => BaseDevice?.Name ?? "-1";

        public int Index { get; private set; } // For socket control, unique

        // name count is the short name for displaying in moning groups
        public string NameCount { get; private set; }
        public bool Enabled { get; protected set; }

        // disabled state check
        public bool IsDisabled => (!Enabled || State == DeviceState.Disabled);

        public DeviceState State { get; set; } = DeviceState.Stopped;

        public string B64Uuid
        {
            get
            {
                //UUIDs
                //RIG - 0
                //CPU - 1
                //GPU - 2 // NVIDIA
                //AMD - 3
                // types 

                int type = 1; // assume type is CPU
                if (DeviceType == DeviceType.NVIDIA)
                {
                    type = 2;
                }
                else if (DeviceType == DeviceType.AMD)
                {
                    type = 3;
                }
                var b64Web = UUID.GetB64UUID(Uuid);
                return $"{type}-{b64Web}";
            }
        }

        public List<Algorithm> AlgorithmSettings { get; protected set; } = new List<Algorithm>();

        public double MinimumProfit { get; set; }

        public string BenchmarkCopyUuid { get; set; }

        #region DeviceMonitor
        public DeviceMonitor DeviceMonitor { get; set; }

        #region Getters

        public uint PowerTarget
        {
            get
            {
                if (DeviceMonitor != null && DeviceMonitor is IPowerTarget get) return get.PowerTarget;
                //throw new NotSupportedException($"Device with {Uuid} doesn't support PowerTarget");
                return 0;
            }
        }
        public PowerLevel PowerLevel
        {
            get
            {
                if (DeviceMonitor != null && DeviceMonitor is IPowerLevel get) return get.PowerLevel;
                return PowerLevel.Unsupported;
            }
        }

        public float Load
        {
            get
            {
                if (DeviceMonitor != null && DeviceMonitor is ILoad get) return get.Load;
                return -1;
            }
        }
        public float Temp
        {
            get
            {
                if (DeviceMonitor != null && DeviceMonitor is ITemp get) return get.Temp;
                return -1;
            }
        }
        public int FanSpeed
        {
            get
            {
                if (DeviceMonitor != null && DeviceMonitor is IFanSpeed get) return get.FanSpeed;
                return -1;
            }
        }
        public double PowerUsage
        {
            get
            {
                if (DeviceMonitor != null && DeviceMonitor is IPowerUsage get) return get.PowerUsage;
                return -1;
            }
        }
        #endregion Getters

        #region Setters

        #endregion
        
        #endregion DeviceMonitor


        // constructor
        public ComputeDevice(BaseDevice baseDevice, int index, string nameCount)
        {
            BaseDevice = baseDevice;
            Index = index;
            NameCount = nameCount;
            SetEnabled(true);
        }

        public void SetEnabled(bool isEnabled)
        {
            Enabled = isEnabled;
            State = isEnabled ? DeviceState.Stopped : DeviceState.Disabled;
        }

        // combines long and short name
        public string GetFullName()
        {
            return $"{NameCount} {Name}";
        }
         
        // TODO double check adding and removing plugin algos
        public void UpdatePluginAlgorithms(string pluginUuid, IList<PluginAlgorithm> pluginAlgos)
        {
            var pluginUuidAlgos = AlgorithmSettings
                .Where(algo => algo is PluginAlgorithm pAlgo && pAlgo.BaseAlgo.MinerID == pluginUuid)
                .Cast<PluginAlgorithm>();

            // filter out old plugin algorithms if any
            if (pluginUuidAlgos.Count() > 0)
            {
                AlgorithmSettings = AlgorithmSettings.Where(algo => pluginUuidAlgos.Contains(algo) == false).ToList();
            }

            // keep old algorithms with settings and filter out obsolete ones
            var newAlgorithmIDs = pluginAlgos.Select(algo => algo.AlgorithmStringID);
            var oldAlgosWithSettings = pluginUuidAlgos.Where(algo => newAlgorithmIDs.Contains(algo.AlgorithmStringID));

            // filter out old algorithms with settings and keep only brand new ones
            var oldAlgosWithSettingsIDs = oldAlgosWithSettings.Select(algo => algo.AlgorithmStringID).ToList();
            var newPluginAlgos = pluginAlgos.Where(algo => oldAlgosWithSettingsIDs.Contains(algo.AlgorithmStringID) == false);
            
            // add back old ones that are in the new module
            if (oldAlgosWithSettings.Count() > 0) AlgorithmSettings.AddRange(oldAlgosWithSettings);
            // add new ones 
            //if (newPluginAlgos.Count() > 0) AlgorithmSettings.AddRange(newPluginAlgos);
            var newPluginAlgosList = newPluginAlgos.ToList();
            foreach (var pluginAlgo in newPluginAlgos)
            {
                AlgorithmSettings.Add(pluginAlgo);
            }
        }

        public void RemovePluginAlgorithms(string pluginUUID)
        {
            var toRemove = AlgorithmSettings.Where(algo => algo is PluginAlgorithm pAlgo && pAlgo.BaseAlgo.MinerID == pluginUUID);
            if (toRemove.Count() == 0) return;
            var newList = AlgorithmSettings.Where(algo => toRemove.Contains(algo) == false).ToList();
            AlgorithmSettings = newList;
        }

        public void CopyBenchmarkSettingsFrom(ComputeDevice copyBenchCDev)
        {
            foreach (var copyFromAlgo in copyBenchCDev.AlgorithmSettings)
            { 
                var setAlgo = AlgorithmSettings.Where(a => a.AlgorithmStringID == copyFromAlgo.AlgorithmStringID).FirstOrDefault();
                if (setAlgo != null)
                {
                    setAlgo.BenchmarkSpeed = copyFromAlgo.BenchmarkSpeed;
                    setAlgo.ExtraLaunchParameters = copyFromAlgo.ExtraLaunchParameters;
                    setAlgo.PowerUsage = copyFromAlgo.PowerUsage;
                }
            }
        }

        public Algorithm GetAlgorithm(string minerUUID, params AlgorithmType[] ids)
        {
            return AlgorithmSettings.Where(a => a.MinerUUID == minerUUID && a.IDs.Except(ids).Count() == 0).FirstOrDefault();
        }

        #region Config Setters/Getters
        
        public void SetDeviceConfig(DeviceConfig config)
        {
            if (config == null || config.DeviceUUID != Uuid) return;
            // set device settings
            //Enabled = config.Enabled;
            SetEnabled(config.Enabled);
            MinimumProfit = config.MinimumProfit;

            if (config.PowerLevel != PowerLevel.Unsupported && config.PowerLevel != PowerLevel.Custom && DeviceMonitor is ISetPowerLevel setPowerLevel)
            {
                //PowerLevel = config.PowerLevel;
                setPowerLevel.SetPowerTarget(config.PowerLevel);
            }
            if (config.PowerLevel == PowerLevel.Custom && DeviceMonitor is ISetPowerTargetPercentage setPowerTargetPercentage)
            {
                //PowerTarget = config.PowerTarget;
                setPowerTargetPercentage.SetPowerTarget(config.PowerTarget);
            }
            


            if (config.PluginAlgorithmSettings == null) return;
            // plugin algorithms
            var pluginAlgos = AlgorithmSettings.Where(algo => algo is PluginAlgorithm).Cast<PluginAlgorithm>();
            foreach (var pluginConf in config.PluginAlgorithmSettings)
            {
                var pluginConfAlgorithmIDs = pluginConf.GetAlgorithmIDs();
                var pluginAlgo = pluginAlgos
                    .Where(pAlgo => pluginConf.PluginUUID == pAlgo.BaseAlgo.MinerID && pluginConfAlgorithmIDs.Except(pAlgo.BaseAlgo.IDs).Count() == 0)
                    .FirstOrDefault();
                if (pluginAlgo == null) continue;
                // set plugin algo
                pluginAlgo.Speeds = pluginConf.Speeds;
                pluginAlgo.Enabled = pluginConf.Enabled;
                pluginAlgo.ExtraLaunchParameters = pluginConf.ExtraLaunchParameters;
                pluginAlgo.PowerUsage = pluginConf.PowerUsage;
                pluginAlgo.ConfigVersion = pluginConf.GetVersion();
            }
        }

        public DeviceConfig GetDeviceConfig()
        {
            var ret = new DeviceConfig
            {
                DeviceName = Name,
                DeviceUUID = Uuid,
                Enabled = Enabled,
                MinimumProfit = MinimumProfit,
                PowerLevel = PowerLevel,
                PowerTarget = PowerTarget
            };
            // init algo settings
            foreach (var algo in AlgorithmSettings)
            {
                if (algo is PluginAlgorithm pluginAlgo)
                {
                    var pluginConf = new PluginAlgorithmConfig
                    {
                        Name = pluginAlgo.PluginName,
                        PluginUUID = pluginAlgo.BaseAlgo.MinerID,
                        AlgorithmIDs = string.Join("-", pluginAlgo.BaseAlgo.IDs.Select(id => id.ToString())),
                        Enabled = pluginAlgo.Enabled,
                        ExtraLaunchParameters = pluginAlgo.ExtraLaunchParameters,
                        PluginVersion = $"{pluginAlgo.PluginVersion.Major}.{pluginAlgo.PluginVersion.Minor}",
                        PowerUsage = pluginAlgo.PowerUsage,
                        Speeds = pluginAlgo.Speeds
                    };
                    ret.PluginAlgorithmSettings.Add(pluginConf);
                }
            }

            return ret;
        }

        #endregion Config Setters/Getters
    }
}
