using System;
using System.IO;
using UnityEngine;

// Writes a single key back into config.txt, preserving comments, ordering and every
// other setting. Used by the in-game tuning menu so a value changed in the headset
// survives the session - and, more importantly, survives the next PollConfig.
//
// That last part is not optional: VRManager.PollConfig re-reads config.txt every 2
// seconds, so an in-memory change that is not written to disk gets reverted within two
// seconds of being made. Anything that tunes a config-backed value MUST go through here.
public static class VRConfigFile
{
    // Alternative spellings accepted by the parser, so a rewrite finds the user's line
    // whichever form they typed rather than appending a duplicate below it.
    static string[] AliasesFor(string key)
    {
        if (key == "screen_fov") return new string[] { "screen_fov", "screenfov", "vr_fov", "fov" };
        if (key == "eye_height") return new string[] { "eye_height", "eyeheight" };
        if (key == "eye_forward") return new string[] { "eye_forward", "eyeforward" };
        if (key == "hand_height") return new string[] { "hand_height", "handheight" };
        if (key == "near_clip") return new string[] { "near_clip", "nearclip" };
        if (key == "hudScale") return new string[] { "hudscale", "hud_scale", "vrhudscale" };
        if (key == "turn_speed") return new string[] { "turn_speed" };
        if (key == "deadzone") return new string[] { "deadzone" };
        return new string[] { key.ToLower() };
    }

    public static void Set(string key, string valueText)
    {
        try { VRManagerLogger.LogInfo("[IMVR] " + key + " -> " + valueText); } catch { }

        string path = VRManager.configPath;
        if (string.IsNullOrEmpty(path)) return; // in-memory change still applies this session

        try
        {
            string[] aliases = AliasesFor(key);
            string[] lines = File.ReadAllLines(path);
            bool found = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("#") || t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string k = t.Substring(0, eq).Trim().ToLower();

                bool match = false;
                for (int a = 0; a < aliases.Length; a++)
                {
                    if (k == aliases[a]) { match = true; break; }
                }
                if (!match) continue;

                // Rewrite EVERY match, not just the first. ParseConfig applies each line in
                // turn so the last one wins; a stale duplicate left behind would let the 2s
                // poll undo what was just set.
                lines[i] = key + "=" + valueText;
                found = true;
            }

            if (found)
            {
                File.WriteAllLines(path, lines);
            }
            else
            {
                File.AppendAllText(path, Environment.NewLine + key + "=" + valueText + Environment.NewLine);
            }
        }
        catch (Exception e)
        {
            try { VRManagerLogger.LogWarn("[IMVR] Could not persist " + key + ": " + e.Message); } catch { }
        }
    }
}
