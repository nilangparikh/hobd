﻿using System;
using System.Text;
using System.Collections.Generic;

namespace hobd
{

/**
 * MIL sensor reads all raised DTC codes and provides alternative interface to fetch them.
 * It works as a regular sensor, however the recommended read interval should be of course relatively big (MILSensor.ReadInterval)
 * Read DTCs are fetched via MILValue property which returs array of strings with detected DTCs.
 */
public class MILSensor : OBD2Sensor
{

    public MILSensor()
    {
        RawCommand = "03";
    }

    public const int ReadInterval = 5*60*1000;

    public override double Value { get{ return 0; } }

    protected string[] mil_value;

    public override bool SetValue(byte[] dataraw)
    {
        /*
        if (dataraw.Length < 1)
            return false;
        */
        this.dataraw = dataraw;

        string r = "";
        for (int i = 0; i < dataraw.Length; i++)
           r += dataraw[i].ToString("X2") + " ";
        Logger.error("MILTrace", "dataraw: " + r);
        
        this.mil_value = null;
        this.TimeStamp = DateTimeMs.Now;

        foreach (string str in this.MILValue)
        {
            Logger.error("MILTrace", "code: " + str);
        }

        registry.TriggerListeners(this);
        return true;
    }

    public virtual string[] MILValue{
        get{
            if (mil_value == null && dataraw != null)
            {
                var l = new List<string>();

                int idx = 0;

                while (idx < dataraw.Length && (dataraw[idx]&0xF0) != 0x40)
                    idx++;

                if (registry.ProtocolId >= 6)
                {
                    int len = 0;
                    while(idx < dataraw.Length-1)
                    {
                        // reply?
                        if (len == 0)
                        {
                            if ((dataraw[idx]&0xF0) == 0x40)
                            {
                                idx++;
                                len = dataraw[idx];
                                idx++;
                                continue;
                            }else{
                                idx++;
                                continue;
                            }
                        }
                        // codes
                        var a = dataraw[idx];
                        var b = dataraw[idx+1];
                        idx+=2;

                        if (a == 0 && b == 0)
                            continue;

                        string mil = (new string[]{"P", "C", "B", "U"})[a>>6];

                        mil += ToChar(((a>>4)&0x3));
                        mil += ToChar(((a>>0)&0xF));
                        
                        mil += ToChar(((b>>4)&0xF));
                        mil += ToChar(((b>>0)&0xF));
                        l.Add(mil);
                        len--;
                    }
                }
                else{
                    for(; idx < dataraw.Length-1; idx +=2)
                    {
                        if (idx % 7 < 2 && (dataraw[idx]&0xF0) == 0x40)
                            idx++;

                        if (idx+1 >= dataraw.Length)
                            continue;

                        var a = dataraw[idx];
                        var b = dataraw[idx+1];

                        if (a == 0 && b == 0)
                            continue;

                        string mil = (new string[]{"P", "C", "B", "U"})[a>>6];

                        mil += ToChar(((a>>4)&0x3));
                        mil += ToChar(((a>>0)&0xF));
                        
                        mil += ToChar(((b>>4)&0xF));
                        mil += ToChar(((b>>0)&0xF));
                        l.Add(mil);
                    }
                }

                mil_value = l.ToArray();
            }
            return mil_value;
        }
    }

    public static char ToChar(int c)
    {
        return c < 0xA ? (char)(0x30+c) : (char)('A'+(char)(c-0xA));
    }

}

}
