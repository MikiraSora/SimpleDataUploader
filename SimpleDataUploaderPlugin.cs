using OsuRTDataProvider;
using OsuRTDataProvider.BeatmapInfo;
using RealTimePPDisplayer;
using RealTimePPDisplayer.Displayer;
using RealTimePPDisplayer.Expression;
using RealTimePPDisplayer.Formatter;
using Sync.Plugins;
using Sync.Tools;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static OsuRTDataProvider.Listen.OsuListenerManager;
using System.Threading;
using System.Net;
using OsuRTDataProvider.Listen;
using Newtonsoft.Json;
using System.Diagnostics;
using static SimpleDataUploader.UploadDataEntity;
using Logger = Sync.Tools.Logger;

namespace SimpleDataUploader
{
    [SyncPluginDependency("8eb9e8e0-7bca-4a96-93f7-6408e76898a9", Require = true)]
    public class SimpleDataUploaderPlugin : Plugin
    {
        private Logger logger = new Logger<SimpleDataUploaderPlugin>();
        private OsuRTDataProviderPlugin ortdp_plugin;
        private RealTimePPDisplayerPlugin rtpp_plugin;
        private Beatmap current_beatmap;
        private static MD5 md5 = MD5.Create();
        private DisplayerBase displayer =new DisplayerBase();
        private Thread upload_thread;
        private PluginConfigurationManager config_manager;

        private Setting setting=new Setting();

        private string osu_folder;
        private HashSet<UploadDataEntity> wait_data_pool = new HashSet<UploadDataEntity>();

        private string OsuFolder => osu_folder ?? (osu_folder = GetOsuFolderDymatic());

        public ConcurrentQueue<UploadDataEntity> upload_queue = new ConcurrentQueue<UploadDataEntity>();
        private double current_ur;
        private string current_player;

        public SimpleDataUploaderPlugin() : base("SimpleDataUploaderPlugin", "DarkProjector")
        {
            config_manager = new PluginConfigurationManager(this);
            config_manager.AddItem(setting);

            EventBus.BindEvent<PluginEvents.LoadCompleteEvent>(OnPluginLoaded);

            upload_thread = new Thread(OnUploadThread);
            upload_thread.IsBackground = true;
            upload_thread.Name = "SimpleDataUploaderPlugin_UploadThread";
            upload_thread.Start();
        }

        private void OnUploadThread()
        {
            while (true)
            {
                if (upload_queue.TryDequeue(out var task_obj))
                {
                    try
                    {
                        logger.LogInfomation($"Try to upload \"{task_obj.ToString()}\"");

                        BackupDataToLocalTemp(task_obj);
                        UploadDataToServer(task_obj);

                        logger.LogInfomation($"upload \"{task_obj.ToString()}\" successfully");
                    }
                    catch (Exception e)
                    {
                        logger.LogInfomation($"upload \"{task_obj.ToString()}\" failed! exception message:{e.Message}");
                    }
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
        }

        private void BackupDataToLocalTemp(UploadDataEntity task_obj)
        {

            if (!Directory.Exists("Uploader_Backup"))
            {
                Directory.CreateDirectory("Uploader_Backup");
            }

            var path = Path.Combine("Uploader_Backup", $"{task_obj.OsuFileHash}-{DateTime.Now.GetHashCode()}.txt");
            File.WriteAllText(path, JsonConvert.SerializeObject(task_obj, Formatting.Indented));
        }

        private void UploadDataToServer(object task_obj)
        {
            if (string.IsNullOrWhiteSpace(setting.UploadUrl))
                return;

            HttpWebRequest request = HttpWebRequest.CreateHttp(setting.UploadUrl);
            request.Method = "POST";

            var body = JsonConvert.SerializeObject(task_obj);

            using (var writer=new StreamWriter(request.GetRequestStream()))
                writer.Write(body);

            var response = request.GetResponse() as HttpWebResponse;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                string message=string.Empty;

                try
                {
                    //get error message if it exist
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        message = reader.ReadToEnd();
                    }
                }
                catch
                {

                }

                throw new Exception($"Status Code : {response.StatusCode} , Message : {message}");
            }
        }

        private void OnPluginLoaded(PluginEvents.LoadCompleteEvent @event)
        {
            //Get ORTDP&RTPP plugin
            ortdp_plugin = @event.Host.EnumPluings().OfType<OsuRTDataProviderPlugin>().FirstOrDefault();
            rtpp_plugin = @event.Host.EnumPluings().OfType<RealTimePPDisplayerPlugin>().FirstOrDefault();

            if (ortdp_plugin==null)
            {
                logger.LogError("Plugin ORTDP is not found and can't able to collect/upload play data. please install ORTDP plugin. just type 'plugins install provider' and restart Sync.");
                return;
            }

            if (rtpp_plugin == null)
            {
                logger.LogError("Plugin RTPP is not found and can't able to collect/upload play data. please install RTPP plugin. just type 'plugins install displayer' and restart Sync.");
                return;
            }

            //bind events.
            ortdp_plugin.ListenerManager.OnStatusChanged += ListenerManager_OnStatusChanged;
            ortdp_plugin.ListenerManager.OnBeatmapChanged += ListenerManager_OnBeatmapChanged;
            ortdp_plugin.ListenerManager.OnPlayerChanged += ListenerManager_OnPlayerChanged;
            ortdp_plugin.ListenerManager.OnErrorStatisticsChanged += ListenerManager_OnErrorStatisticsChanged;

            //add a shadow display for getting data easy from rtpp plugin.
            var shadow_displayer_name = "cute_bunny";
            Func<int?, DisplayerBase> displayer_creator = new Func<int?, DisplayerBase>(id => displayer);

            rtpp_plugin.RegisterDisplayer(shadow_displayer_name, displayer_creator);

            var add_displayer_method=typeof(RealTimePPDisplayerPlugin).GetMethod("AddDisplayer", BindingFlags.NonPublic|BindingFlags.Instance);
            add_displayer_method.Invoke(rtpp_plugin, new object[] { shadow_displayer_name });

            logger.LogInfomation("Plugin is ready.");
        }

        private void ListenerManager_OnErrorStatisticsChanged(ErrorStatisticsResult result)
        {
            current_ur = result.UnstableRate;
        }

        private void ListenerManager_OnPlayerChanged(string player)
        {
            current_player = player;
        }

        private void ListenerManager_OnBeatmapChanged(OsuRTDataProvider.BeatmapInfo.Beatmap map)
        {
            current_beatmap = map;

            if (current_beatmap==null || current_beatmap == Beatmap.Empty)
            {
                logger.LogWarning("Current beatmap is null or empty.");
            }
            else
            {
                logger.LogWarning($"Current beatmap:{current_beatmap?.BeatmapSetID} {current_beatmap?.Artist} - {current_beatmap?.Title}[{current_beatmap?.Difficulty}]({current_beatmap?.BeatmapID})");
            }
        }

        private void ListenerManager_OnStatusChanged(OsuStatus last_status, OsuStatus status)
        {
            if (status == last_status)
                return;

            if (status==OsuStatus.Rank)
            {
                //collect data and post to uploading queue.
                //todo
                try
                {
                    var data = new
                    UploadDataEntity{
                        BeatmapInfo = new BeatmapInfoEntity()
                        {
                            Artist=current_beatmap?.Artist,
                            ArtistUnicode=current_beatmap?.ArtistUnicode,
                            Title=current_beatmap?.Title,
                            TitleUnicode=current_beatmap?.TitleUnicode,
                            Version=current_beatmap?.Version,
                            Creator=current_beatmap?.Creator
                        },
                        OsuFileHash = CalculateHash(),
                        PP = displayer.Pp.RealTimePP,
                        StarRatio = displayer.BeatmapTuple.Stars,
                        ACC = CalculateAcc(),
                        UR = current_ur,
                        BeatmapID=current_beatmap?.BeatmapID,
                        BeatmapSetID=current_beatmap?.BeatmapSetID,
                        PlayerName = current_player,
                        Score= ortdp_plugin.ListenerManager.GetCurrentData(ProvideDataMask.Score).Score
                    };

                    PostData(data);
                }
                catch (Exception e)
                {
                    logger.LogError($"collect data failed!.exception message:{e.Message}");
                    return;
                }
            }
        }

        private string CalculateHash()
        {
            var bytes = File.ReadAllBytes(current_beatmap.FilenameFull);
            var result = md5.ComputeHash(bytes);

            var sb = new StringBuilder();

            foreach (var b in result)
            {      
                sb.Append(b.ToString("X2"));
            }

            return sb.ToString();
        }

        private double? CalculateAcc()
        {
            var hit = displayer.HitCount;
            double? acc = null;

            switch (displayer.Mode)
            {
                case OsuRTDataProvider.Listen.OsuPlayMode.Osu:
                    acc = (hit.Count300 * 300 + hit.Count100 * 100 + hit.Count50 * 50)*1.0 / (300 * (hit.CountMiss + hit.Count300 + hit.Count50 + hit.Count100));
                    break;
                case OsuRTDataProvider.Listen.OsuPlayMode.Taiko:
                    acc = (0.5 * hit.Count100 + hit.Count300)*1.0 / (hit.CountMiss + hit.Count300 + hit.Count100);
                    break;
                case OsuRTDataProvider.Listen.OsuPlayMode.CatchTheBeat:
                    break;
                case OsuRTDataProvider.Listen.OsuPlayMode.Mania:
                    break;
                case OsuRTDataProvider.Listen.OsuPlayMode.Unknown:
                    break;
                default:
                    break;
            }

            return acc==null?acc:acc*100;
        }

        private void PostData(UploadDataEntity data)
        {
            upload_queue.Enqueue(data);

#if DEBUG
            logger.LogInfomation("PostData:" + data.ToString());
#endif
        }

        public bool TryGetUserName(out string user_name)
        {
            user_name = "";
            try
            {
                var processes = Process.GetProcessesByName(@"osu!");

                if (processes.Length != 0)
                {
                    string osu_path = processes[0].MainModule.FileName.Replace(@"osu!.exe", string.Empty);

                    string osu_config_file = Path.Combine(osu_path, $"osu!.{Environment.UserName}.cfg");
                    var lines = File.ReadLines(osu_config_file);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Username"))
                        {
                            user_name = WindowsPathStrip(line.Split('=')[1].Trim());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError("Failed to get user name from osu! config files :" + e.Message);
            }

            return !string.IsNullOrWhiteSpace(user_name);

            string WindowsPathStrip(string entry)
            {
                StringBuilder builder = new StringBuilder(entry);
                foreach (char c in Path.GetInvalidFileNameChars())
                    builder.Replace(c.ToString(), string.Empty);
                builder.Replace(".", string.Empty);
                return builder.ToString();
            }
        }

        private string GetOsuFolderDymatic()
        {
            var processes = Process.GetProcessesByName(@"osu!");

            if (processes.Length != 0)
                return processes[0].MainModule.FileName.Replace(@"osu!.exe", string.Empty);

            throw new Exception("osu! not found.");
        }
    }
}
