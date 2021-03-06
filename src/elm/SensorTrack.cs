namespace hobd{

using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using System.Linq;


/**
 * structure used per-sensor to store sensor history data and other related information
 */
public class SensorTrackData
{
    public string id;
    public int period;
    public int gap;
    public int length;

    public long stopat;
    public long startat;

    public long last_stored, LastTimeStamp;

    public List<long> history_t = new List<long>();
    public List<double> history_v = new List<double>();
}

/**
 * Storage of sensor logs.
 * Uses configuration file to know which sensors should be logged and when
 */
public class SensorTrack
{
    protected SensorRegistry Registry;
    protected string DataPath;

    public bool TrackAccum = false;
    public bool TrackPassive = false;
    Dictionary<string, SensorTrackData> Settings = new Dictionary<string, SensorTrackData>();

    public const string VersionID = "track0.";

    public SensorTrack(string dataPath)
    {
        this.DataPath = dataPath;
        try{
            if (!Directory.Exists(DataPath))
                Directory.CreateDirectory(DataPath);
        }catch(Exception e){
            Logger.error("SensorTrack", "CreateDirectory", e);
        }
    }

    public void LoadConfig(string configPath)
    {
        XmlReaderSettings xrs = new XmlReaderSettings();
        xrs.IgnoreWhitespace = true;
        xrs.IgnoreComments = true;

        try{
            XmlReader reader = XmlReader.Create(configPath, xrs);
            
            reader.ReadStartElement("sensor-track");
            var id = reader.GetAttribute("id");
            var name = reader.GetAttribute("name");

            while(true){
                if (reader.NodeType != XmlNodeType.Element){
                    if (!reader.Read())
                        break;
                    continue;
                }
                switch (reader.Name) {
                    case "track-passive":
                        this.TrackPassive = "true" == reader.ReadElementContentAsString();
                        break;
                    case "track-accumulator":
                        this.TrackAccum = "true" == reader.ReadElementContentAsString();
                        break;
                    case "track":
                        SensorTrackData set = new SensorTrackData();
                        set.id = reader.GetAttribute("sensor");
                        set.period = ParseTimeSpan(reader.GetAttribute("period"));
                        set.gap = ParseTimeSpan(reader.GetAttribute("gap"));
                        set.length = ParseTimeSpan(reader.GetAttribute("length"));
                        if (set.id != null && !Settings.ContainsKey(set.id))
                            Settings.Add(set.id, set);
                        reader.Read();
                        break;
                    default:
                        reader.Read();
                        break;
                }
            }
            reader.Close();
        }catch(FileNotFoundException){
        }catch(Exception e){
            Logger.error("SensorTrack", "failed init", e);
        }
    }

    public static int ParseTimeSpan(string val)
    {
        if (val == null)
            return 0;
        if (val.EndsWith("sec"))
        {
            return int.Parse(val.Substring(0, val.Length-3)) * 1000;
        }
        if (val.EndsWith("min"))
        {
            return int.Parse(val.Substring(0, val.Length-3)) * 1000 * 60;
        }
        if (val.EndsWith("hour"))
        {
            return int.Parse(val.Substring(0, val.Length-4)) * 1000 * 60 * 60;
        }
        if (val.EndsWith("hours"))
        {
            return int.Parse(val.Substring(0, val.Length-5)) * 1000 * 60 * 60;
        }
        return int.Parse(val);
    }

    public virtual void Attach(SensorRegistry registry)
    {
        if (this.Registry != null)
            throw new Exception("Can't attach: Invalid State");
        this.Registry = registry;

        Settings.Keys.ToList().ForEach((id) => {
            var set = Settings[id];
            var sensor = Registry.Sensor(id);
            if (sensor == null)
                return;
            if (set.id != sensor.ID)
            {
                set.id = sensor.ID;
                Settings.Remove(id);
                Settings.Add(set.id, set);
            }
            Logger.trace("SensorTrack", "attach " +set.id + " period " + set.period + " length " + set.length + " gap "+ set.gap);
            try{
                Registry.AddListener(sensor, this.SensorChanged, set.period);
            }catch(Exception){
                Logger.trace("SensorTrack", "attach failed" +sensor.ID);
            }
        });

        if (this.TrackPassive)
        {
            Registry.AddPassiveListener(this.SensorChanged);
        }
        if (this.TrackAccum)
        {
            foreach(var s in Registry.Sensors.Where(s => s is IAccumulatorSensor)){
                try{
                    Registry.AddListener(s, this.SensorChanged);
                }catch(Exception){
                    Logger.trace("SensorTrack", "attach failed" +s.ID);
                }
            }
        }
    }

    protected virtual void SensorChanged(Sensor sensor)
    {
        SensorTrackData set;
        if (!Settings.TryGetValue(sensor.ID, out set))
        {
            set = new SensorTrackData();
            set.id = sensor.ID;
            Settings.Add(set.id, set);
        }

        if (set.LastTimeStamp == sensor.TimeStamp)
            return;

        if (Logger.DUMP) Logger.dump("SensorTrack", "SensorChanged " +sensor.ID);

        lock(set)
        {
            if (set.length > 0 && set.stopat == 0)
            {
                set.stopat = sensor.TimeStamp + set.length;
            }
            if (set.stopat != 0 && sensor.TimeStamp >= set.stopat)
            {
                set.startat = sensor.TimeStamp + set.gap;
                set.stopat = 0;
                Registry.RemoveListener(sensor, this.SensorChanged);
                //TODO: raise timer
            }

            StoreSensor(sensor);
        }
    }

    protected virtual void StoreSensor(Sensor sensor)
    {
        var set = Settings[sensor.ID];

        set.LastTimeStamp = sensor.TimeStamp;
        // TODO: very rough.. extract logic via IAdaptable?
        if (sensor is IAccumulatorSensor && set.history_t.Count > 0 && set.history_v[set.history_t.Count-1] <= sensor.Value)
        {
            set.history_t[set.history_t.Count-1] = sensor.TimeStamp;
            set.history_v[set.history_t.Count-1] = sensor.Value;
        }else{
            set.history_t.Add(sensor.TimeStamp);
            set.history_v.Add(sensor.Value);
        }
    }

    public virtual void Store()
    {
        lock(this)
        {
            foreach(var id in Settings.Keys)
            {
                var set = Settings[id];
                StoreSensorData(set);
            }
        }
    }
    
    protected virtual void StoreSensorData(SensorTrackData set)
    {
         lock(set)
         {
             try
             {
                 var fs = new FileStream(Path.Combine(DataPath, VersionID + set.id), FileMode.Append);
                 var sw = new BinaryWriter(fs);

                 for(int i = 0; i < set.history_t.Count(); i++)
                 {
                     sw.Write(set.history_t[i]);
                     sw.Write(set.history_v[i]);
                 }
                 set.history_t.Clear();
                 set.history_v.Clear();
                 sw.Close();
                 fs.Close();
             }catch(Exception e){
                 Logger.error("StoreSensorData", "fail", e);
             }
         }
    }

    public virtual void Detach()
    {
        if (Registry == null) return;

        try{
            Registry.RemoveListener(this.SensorChanged);
            Registry.RemovePassiveListener(this.SensorChanged);
        }catch(Exception e){
            Logger.error("SensorTrack", "Detach fail", e);
        }
        Store();
        this.Registry = null;
    }

    public virtual bool FetchHistory(Sensor sensor, out long[] timestamps, out double[] values)
    {
        // TODO
        timestamps = null;
        values = null;
        return false;
    }

}

}