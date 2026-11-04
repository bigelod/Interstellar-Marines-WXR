using UnityEngine;

// ONE helmet lamp, owned by the mod, parented to the VR camera.
//
// ===== THE BUG THIS REPLACES =====
// Every weapon carries its own WeaponLight, and WeaponLight.Awake instantiates its own
// flashLightGO. The old patch reparented *each* of those to the head camera as its
// LateUpdate ran, so every weapon swap added another live lamp to the player's face.
// Brightness stacked, and because the stowed weapons' WeaponLight state was never
// toggled, the extra lamps could not be switched off - exactly the "stacked and got
// brighter and brighter, and only fixed after I respawned" report. Respawn fixed it
// because the whole Helmet and its camera were rebuilt, taking the orphans with them.
//
// So: take one copy, hold it, and keep every gun's own lamp switched off forever. A
// side benefit is that the light no longer blinks out mid-swap, because it is not tied
// to the weapon being drawn.
public static class VRHeadLamp
{
    static GameObject lamp;
    static Camera ownerCam;

    // Rebuilt when the camera changes - the Helmet and its camera are destroyed and
    // recreated on every level load and every respawn, which would otherwise leave the
    // lamp parented to a dead transform.
    public static void Ensure(GameObject source, Camera headCam)
    {
        if (source == null || headCam == null) return;

        if (lamp != null && ownerCam == headCam)
        {
            Place();
            return;
        }

        if (lamp != null) { GameObject.Destroy(lamp); lamp = null; }

        lamp = GameObject.Instantiate(source) as GameObject;
        if (lamp == null) return;
        lamp.name = "IMVR_HeadLamp";
        ownerCam = headCam;
        lamp.transform.SetParent(headCam.transform, false);
        lamp.transform.localRotation = Quaternion.identity;

        // The prefab carries billboard geometry for the bulb - that is the glowing orb.
        // Keep the Light, drop everything visible.
        try
        {
            Renderer[] rs = lamp.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) { if (rs[i] != null) rs[i].enabled = false; }
            Transform[] ts = lamp.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < ts.Length; i++)
            {
                if (ts[i] == null) continue;
                Behaviour halo = ts[i].gameObject.GetComponent("Halo") as Behaviour;
                if (halo != null) halo.enabled = false;
                Behaviour flare = ts[i].gameObject.GetComponent("LensFlare") as Behaviour;
                if (flare != null) flare.enabled = false;
            }
        }
        catch { }

        Place();
        VRManagerLogger.LogInfo("[IMVR] Head lamp created from '" + source.name + "' on camera '" + headCam.name + "'");
    }

    static void Place()
    {
        if (lamp == null) return;
        // Re-applied every frame so the tuning menu moves it live.
        lamp.transform.localPosition = new Vector3(0f, VRManager.lightHeight, VRManager.lightForward);
        lamp.transform.localRotation = Quaternion.identity;
    }

    public static void SetOn(bool on)
    {
        if (lamp == null) return;
        if (lamp.activeSelf != on) lamp.SetActive(on);
    }

    public static bool Exists() { return lamp != null; }

    // For VRLightTamer, which silences the shadows of every light in the level EXCEPT
    // the ones under this object.
    public static GameObject LampRoot { get { return lamp; } }
}
