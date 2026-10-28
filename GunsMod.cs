using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: MelonInfo(typeof(GBGuns.GunsMod), "Gang Beasts Guns", "1.0.0", "Claude")]

namespace GBGuns
{
    // ------------------------------------------------------------------
    //  Wejscie: mysz (stary Input albo Input System) + pad (Input System), przez refleksje
    //  - tak samo jak w modzie Grab, bo w IL2CPP nie wiadomo, ktory system gra uzywa.
    // ------------------------------------------------------------------
    internal static class Inp
    {
        private static int mode;               // 0 = nie sprawdzono, 1 = stary Input, 2 = Input System, 3 = brak
        private static bool legacyTried, typesScanned;
        private static int fails, padFails;
        private static MethodInfo mGetBtn, mGetKeyDown;
        private static PropertyInfo pMousePos;
        private static Type tMouse, tKeyboard, tGamepad;
        private static readonly Dictionary<string, PropertyInfo> propCache = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<Type, MethodInfo> readCache = new Dictionary<Type, MethodInfo>();
        private static Vector2 lastPos;
        private static bool hasLastPos;

        public static bool Left, Right, F5, F6, F7, PadFire, PadPresent, MouseMoved;
        public static Vector2 Pos, PadAim;
        public static string[] PadButtons = new string[] { "buttonWest", "buttonEast" };

        public static string ModeName
        {
            get { return mode == 1 ? "stary Input" : mode == 2 ? "Input System" : mode == 3 ? "BRAK" : "?"; }
        }

        public static void Poll()
        {
            Left = Right = F5 = F6 = F7 = PadFire = MouseMoved = false;
            PadAim = Vector2.zero;
            if (mode == 0) Resolve();

            if (mode == 1)
            {
                try { LegacyPoll(); fails = 0; }
                catch
                {
                    fails++;
                    if (fails >= 5)
                    {
                        fails = 0;
                        mode = 0;
                        legacyTried = true;
                        Resolve();
                    }
                }
            }
            else if (mode == 2)
            {
                try { NewPoll(); fails = 0; }
                catch
                {
                    fails++;
                    if (fails >= 60) { mode = 3; MelonLogger.Warning("[Guns] Nie moge odczytac myszy."); }
                }
            }

            if (hasLastPos && Vector2.Distance(lastPos, Pos) > 2f) MouseMoved = true;
            lastPos = Pos;
            hasLastPos = true;

            PadPoll();
        }

        private static void ScanTypes()
        {
            if (typesScanned) return;
            typesScanned = true;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (pass == 0 && !asm.GetName().Name.Contains("InputSystem")) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    catch { continue; }
                    foreach (var ty in types)
                    {
                        if (ty == null) continue;
                        string fn = ty.FullName;
                        if (fn == "UnityEngine.InputSystem.Mouse") tMouse = ty;
                        else if (fn == "UnityEngine.InputSystem.Keyboard") tKeyboard = ty;
                        else if (fn == "UnityEngine.InputSystem.Gamepad") tGamepad = ty;
                    }
                }
                if (tMouse != null && tKeyboard != null && tGamepad != null) break;
            }
            MelonLogger.Msg("[Guns] Input System: mysz=" + (tMouse != null) + ", klawiatura=" + (tKeyboard != null) + ", pad=" + (tGamepad != null));
        }

        private static void Resolve()
        {
            if (!legacyTried)
            {
                legacyTried = true;
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("UnityEngine")) continue;
                    t = asm.GetType("UnityEngine.Input", false);
                    if (t != null) break;
                }
                if (t != null)
                {
                    mGetBtn = t.GetMethod("GetMouseButton", new[] { typeof(int) });
                    mGetKeyDown = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                    pMousePos = t.GetProperty("mousePosition");
                    if (mGetBtn != null && pMousePos != null)
                    {
                        mode = 1;
                        MelonLogger.Msg("[Guns] mysz: stary Input");
                        return;
                    }
                }
            }

            ScanTypes();
            if (tMouse != null)
            {
                mode = 2;
                MelonLogger.Msg("[Guns] mysz: Input System");
            }
            else
            {
                mode = 3;
                MelonLogger.Warning("[Guns] Nie znaleziono zadnego systemu wejscia dla myszy.");
            }
        }

        private static bool KeyDown(KeyCode k)
        {
            if (mGetKeyDown == null) return false;
            return (bool)mGetKeyDown.Invoke(null, new object[] { k });
        }

        private static void LegacyPoll()
        {
            Left = (bool)mGetBtn.Invoke(null, new object[] { 0 });
            Right = (bool)mGetBtn.Invoke(null, new object[] { 1 });
            Vector3 mp = (Vector3)pMousePos.GetValue(null, null);
            Pos = new Vector2(mp.x, mp.y);
            F5 = KeyDown(KeyCode.F5);
            F6 = KeyDown(KeyCode.F6);
            F7 = KeyDown(KeyCode.F7);
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            string key = t.FullName + "." + name;
            PropertyInfo p;
            if (!propCache.TryGetValue(key, out p))
            {
                p = t.GetProperty(name);
                propCache[key] = p;
            }
            return p == null ? null : p.GetValue(o, null);
        }

        private static object Static(Type t, string name)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            return p == null ? null : p.GetValue(null, null);
        }

        private static object Read(object control)
        {
            if (control == null) return null;
            Type t = control.GetType();
            MethodInfo m;
            if (!readCache.TryGetValue(t, out m))
            {
                m = t.GetMethod("ReadValue", Type.EmptyTypes);
                readCache[t] = m;
            }
            return m == null ? null : m.Invoke(control, null);
        }

        private static bool Flag(object control, string name)
        {
            object v = Prop(control, name);
            return v is bool && (bool)v;
        }

        private static void NewPoll()
        {
            object ms = Static(tMouse, "current");
            if (ms != null)
            {
                Left = Flag(Prop(ms, "leftButton"), "isPressed");
                Right = Flag(Prop(ms, "rightButton"), "isPressed");
                object pv = Read(Prop(ms, "position"));
                if (pv is Vector2) Pos = (Vector2)pv;
            }
            if (tKeyboard != null)
            {
                object kb = Static(tKeyboard, "current");
                if (kb != null)
                {
                    F5 = Flag(Prop(kb, "f5Key"), "wasPressedThisFrame");
                    F6 = Flag(Prop(kb, "f6Key"), "wasPressedThisFrame");
                    F7 = Flag(Prop(kb, "f7Key"), "wasPressedThisFrame");
                }
            }
        }

        private static void PadPoll()
        {
            if (padFails >= 60) return;
            ScanTypes();
            if (tGamepad == null) return;
            try
            {
                object gp = Static(tGamepad, "current");
                PadPresent = gp != null;
                if (gp == null) return;
                object rs = Read(Prop(gp, "rightStick"));
                if (rs is Vector2) PadAim = (Vector2)rs;
                for (int i = 0; i < PadButtons.Length; i++)
                {
                    if (Flag(Prop(gp, PadButtons[i]), "isPressed")) { PadFire = true; break; }
                }
                padFails = 0;
            }
            catch
            {
                padFails++;
                if (padFails >= 60) MelonLogger.Warning("[Guns] Nie moge odczytac pada - celowanie/strzal z pada niedostepne.");
            }
        }
    }

    // ------------------------------------------------------------------
    //  Efekty
    // ------------------------------------------------------------------
    internal class FxItem
    {
        public GameObject Go;
        public Transform T;
        public float Born, Life, S0, S1;
        public int Kind;            // 0 = kula (rosnie i znika), 1 = smuga pocisku
        public float Thick;
    }

    internal class Debris
    {
        public Transform T;
        public Vector3 V;
        public float Die;
    }

    // ------------------------------------------------------------------
    //  Jeden uzbrojony ludzik
    // ------------------------------------------------------------------
    internal class Shooter
    {
        public Component Actor;
        public Transform Top, Hips, Chest, Head, Hand;
        public float Unit = 0.4f;
        public float Hp;
        public bool Dead;
        public bool LooksLocal;
        public int Order;

        public GameObject Gun;
        public Transform GunT;
        public Vector3 Aim = Vector3.forward;
        public float NextShot;
        public float BotNext;
        public readonly Dictionary<int, int> LimbHits = new Dictionary<int, int>();

        public bool Valid
        {
            get { return Actor != null && Top != null && Hips != null; }
        }

        public Vector3 Origin()
        {
            Transform b = Chest != null ? Chest : Hips;
            Vector3 p = b.position;
            if (Head != null) p += (Head.position - p) * 0.35f;
            return p;
        }
    }

    public class GunsMod : MelonMod
    {
        private static MelonPreferences_Entry<bool> PEnabled, PForceCursor, PBots, PShowDot, PDismember;
        private static MelonPreferences_Entry<float> PHealth, PDamage, PHeadMult, PFireRate, PRange, PKnock;
        private static MelonPreferences_Entry<float> PBotSpread, PBotInterval, PVolume, PGunSize, PStick;
        private static MelonPreferences_Entry<int> PPlayer, PLimbHits;
        private static MelonPreferences_Entry<string> PPadButtons;

        private readonly Dictionary<int, Shooter> shooters = new Dictionary<int, Shooter>();
        private readonly HashSet<int> bad = new HashSet<int>();
        private readonly List<FxItem> fx = new List<FxItem>();
        private readonly List<Debris> debris = new List<Debris>();

        private Type actorType;
        private bool actorTypeSearched, loggedNoActor;
        private float scanTimer;
        private int orderCounter;
        private bool errLogged, lateErrLogged, scanErrLogged;

        private Camera cam;
        private float camTimer;
        private bool manualPick;
        private float lastStickTime = -99f;
        private GameObject dot;
        private Vector3 dotPos;
        private bool dotVisible;

        private static Shader cachedShader;
        private static bool shaderSearched;
        private static AudioClip shotClip;
        private static bool shotTried;
        private static MethodInfo findObjectsMethod;
        private static bool findObjectsResolved, findObjectsNeedsBool;

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBGuns", "Gang Beasts Guns");
            PEnabled = cat.CreateEntry("Enabled", true);
            PForceCursor = cat.CreateEntry("ForceCursor", true);
            PBots = cat.CreateEntry("OthersShoot", true);
            PShowDot = cat.CreateEntry("ShowAimDot", true);
            PDismember = cat.CreateEntry("Dismember", true);
            PHealth = cat.CreateEntry("Health", 100f);
            PDamage = cat.CreateEntry("Damage", 25f);
            PHeadMult = cat.CreateEntry("HeadshotMultiplier", 2f);
            PFireRate = cat.CreateEntry("FireInterval", 0.25f);
            PRange = cat.CreateEntry("Range", 80f);
            PKnock = cat.CreateEntry("Knockback", 6f);
            PBotSpread = cat.CreateEntry("OthersSpreadDegrees", 10f);
            PBotInterval = cat.CreateEntry("OthersShootInterval", 1.8f);
            PVolume = cat.CreateEntry("Volume", 0.6f);
            PGunSize = cat.CreateEntry("GunSize", 1f);
            PStick = cat.CreateEntry("StickDeadzone", 0.35f);
            PPlayer = cat.CreateEntry("PlayerIndex", 0);
            PLimbHits = cat.CreateEntry("HitsToTearOffLimb", 2);
            PPadButtons = cat.CreateEntry("PadFireButtons", "buttonWest,buttonEast");

            ApplyPadButtons();
            LoggerInstance.Msg("Gang Beasts Guns 1.0 zaladowany. Mysz: celuj kursorem, LPM/PPM = strzal. Pad: prawy drazek = celowanie, X/B = strzal. F5 = zmien, ktora postac jest Twoja, F6 wl/wyl, F7 inni strzelaja wl/wyl.");
        }

        private static void ApplyPadButtons()
        {
            try
            {
                var parts = PPadButtons.Value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) Inp.PadButtons = parts;
            }
            catch { }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (var kv in shooters) DestroyGun(kv.Value);
            shooters.Clear();
            bad.Clear();
            cam = null;
            camTimer = 0f;
            scanTimer = 0f;
            manualPick = false;
            ClearFx();
            if (dot != null) { try { UnityEngine.Object.Destroy(dot); } catch { } dot = null; }
        }

        // ---------- Petla glowna ----------

        public override void OnUpdate()
        {
            try
            {
                Inp.Poll();

                if (Inp.F6)
                {
                    PEnabled.Value = !PEnabled.Value;
                    LoggerInstance.Msg("Guns: " + (PEnabled.Value ? "WLACZONY" : "wylaczony"));
                    SavePrefs();
                }
                if (Inp.F7)
                {
                    PBots.Value = !PBots.Value;
                    LoggerInstance.Msg("Inni strzelaja: " + (PBots.Value ? "TAK" : "nie"));
                    SavePrefs();
                }
                if (Inp.F5)
                {
                    manualPick = true;
                    PPlayer.Value = PPlayer.Value + 1;
                    SavePrefs();
                }

                float dt = Time.deltaTime;
                TickFx(dt);

                if (!PEnabled.Value)
                {
                    SetDot(false, Vector3.zero);
                    return;
                }

                if (PForceCursor.Value)
                {
                    try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
                }

                scanTimer -= Time.unscaledDeltaTime;
                if (scanTimer <= 0f)
                {
                    scanTimer = 1f;
                    Scan();
                }

                cam = GetCam();
                RemoveInvalid();

                Shooter player = GetPlayer();
                bool dotOn = false;
                Vector3 dotAt = Vector3.zero;

                foreach (var kv in shooters)
                {
                    Shooter s = kv.Value;
                    if (s.Dead || !s.Valid) continue;
                    if (s == player) UpdatePlayer(s, out dotOn, out dotAt);
                    else if (PBots.Value) UpdateBot(s, dt);
                    else IdleAim(s, dt);
                }

                SetDot(dotOn && PShowDot.Value, dotAt);
            }
            catch (Exception e)
            {
                if (!errLogged)
                {
                    errLogged = true;
                    LoggerInstance.Error("Blad w OnUpdate (kolejne takie same pomijam): " + e);
                }
            }
        }

        public override void OnLateUpdate()
        {
            try
            {
                if (!PEnabled.Value) return;
                foreach (var kv in shooters)
                {
                    Shooter s = kv.Value;
                    if (!s.Valid) continue;
                    if (s.Dead) continue;
                    PlaceGun(s);
                }
            }
            catch (Exception e)
            {
                if (!lateErrLogged)
                {
                    lateErrLogged = true;
                    LoggerInstance.Error("Blad w OnLateUpdate (kolejne takie same pomijam): " + e);
                }
            }
        }

        private static void SavePrefs()
        {
            try { MelonPreferences.Save(); } catch { }
        }

        // ---------- Gracz i boty ----------

        private Shooter GetPlayer()
        {
            if (shooters.Count == 0) return null;
            var list = new List<Shooter>(shooters.Values);
            list.Sort((a, b) => a.Order.CompareTo(b.Order));

            if (!manualPick)
            {
                foreach (var s in list)
                    if (s.LooksLocal && !s.Dead && s.Valid) return s;
            }
            int n = list.Count;
            int idx = ((PPlayer.Value % n) + n) % n;
            Shooter pick = list[idx];
            return pick.Dead ? null : pick;
        }

        private void UpdatePlayer(Shooter s, out bool dotOn, out Vector3 dotAt)
        {
            dotOn = false;
            dotAt = Vector3.zero;
            Vector3 origin = s.Origin();
            Vector3 desired = s.Aim;
            Vector3 point = origin + s.Aim * 5f;

            Vector2 st = Inp.PadAim;
            bool stickActive = st.magnitude > PStick.Value && cam != null;
            if (stickActive)
            {
                Vector3 f = cam.transform.forward; f.y = 0f;
                Vector3 r = cam.transform.right; r.y = 0f;
                if (f.sqrMagnitude > 0.0001f && r.sqrMagnitude > 0.0001f)
                {
                    Vector3 d = r.normalized * st.x + f.normalized * st.y;
                    if (d.sqrMagnitude > 0.0001f)
                    {
                        desired = d.normalized;
                        point = origin + desired * 5f;
                        lastStickTime = Time.time;
                    }
                }
            }
            else if (Time.time - lastStickTime < 2.5f && !Inp.MouseMoved)
            {
                desired = s.Aim;
                point = origin + desired * 5f;
            }
            else if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(new Vector3(Inp.Pos.x, Inp.Pos.y, 0f));
                Plane pl = new Plane(Vector3.up, origin);
                float en;
                if (pl.Raycast(ray, out en))
                {
                    Vector3 hitPt = ray.GetPoint(en);
                    Vector3 d = hitPt - origin;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.04f)
                    {
                        desired = d.normalized;
                        point = hitPt;
                    }
                }
            }

            s.Aim = desired;
            dotOn = true;
            dotAt = point;

            bool fire = Inp.Left || Inp.Right || Inp.PadFire;
            if (fire && Time.time >= s.NextShot) Shoot(s, desired);
        }

        private void UpdateBot(Shooter s, float dt)
        {
            Shooter target = NearestTarget(s);
            Vector3 origin = s.Origin();
            if (target != null)
            {
                Vector3 d = target.Origin() - origin;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                    s.Aim = SmoothAim(s.Aim, d.normalized, dt);

                if (Time.time >= s.BotNext)
                {
                    float interval = Mathf.Max(0.3f, PBotInterval.Value);
                    s.BotNext = Time.time + interval * UnityEngine.Random.Range(0.6f, 1.5f);
                    float dist = Vector3.Distance(target.Hips.position, s.Hips.position);
                    if (dist < 25f)
                    {
                        float ang = UnityEngine.Random.Range(-PBotSpread.Value, PBotSpread.Value);
                        Vector3 dir = Quaternion.Euler(0f, ang, 0f) * s.Aim;
                        Shoot(s, dir);
                    }
                }
            }
            else
            {
                IdleAim(s, dt);
            }
        }

        private void IdleAim(Shooter s, float dt)
        {
            Vector3 f = s.Hips.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.01f) s.Aim = SmoothAim(s.Aim, f.normalized, dt);
        }

        private static Vector3 SmoothAim(Vector3 cur, Vector3 want, float dt)
        {
            float k = 1f - Mathf.Exp(-14f * dt);
            Vector3 v = Vector3.Slerp(cur, want, k);
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : want;
        }

        private Shooter NearestTarget(Shooter me)
        {
            Shooter best = null;
            float bd = float.MaxValue;
            foreach (var kv in shooters)
            {
                Shooter o = kv.Value;
                if (o == me || o.Dead || !o.Valid) continue;
                float d = (o.Hips.position - me.Hips.position).sqrMagnitude;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        // ---------- Strzal ----------

        private void Shoot(Shooter s, Vector3 dir)
        {
            s.NextShot = Time.time + Mathf.Max(0.05f, PFireRate.Value);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            float scale = s.Unit / 0.4f * PGunSize.Value;
            Vector3 muzzle = (s.GunT != null ? s.GunT.position : s.Origin()) + dir * (0.42f * scale);
            Vector3 aimOrigin = s.Origin();
            muzzle.y = aimOrigin.y;

            PlayShot(muzzle);
            SpawnBall(muzzle, new Color(1f, 0.85f, 0.3f), 0.12f, 0.4f * scale, 0.07f);

            Vector3 o = muzzle;
            float remain = PRange.Value;
            Vector3 end = muzzle + dir * remain;

            for (int i = 0; i < 8; i++)
            {
                Ray ray = new Ray(o, dir);
                RaycastHit h;
                if (!Physics.Raycast(ray, out h, remain, ~0, QueryTriggerInteraction.Ignore)) break;
                if (h.collider == null) break;
                Transform ht = h.collider.transform;
                if (ht.IsChildOf(s.Top))
                {
                    o = h.point + dir * 0.03f;
                    remain -= h.distance + 0.03f;
                    if (remain <= 0f) break;
                    continue;
                }
                end = h.point;
                ApplyHit(s, h, ht, dir);
                break;
            }

            SpawnTracer(muzzle, end);
        }

        private void ApplyHit(Shooter shooter, RaycastHit h, Transform ht, Vector3 dir)
        {
            Shooter victim = null;
            foreach (var kv in shooters)
            {
                Shooter v = kv.Value;
                if (v == shooter || !v.Valid) continue;
                if (ht.IsChildOf(v.Top)) { victim = v; break; }
            }

            Rigidbody rb = null;
            try { rb = h.rigidbody; } catch { }
            if (rb != null && !rb.isKinematic)
            {
                try
                {
                    if (rb.IsSleeping()) rb.WakeUp();
                    rb.AddForce((dir + Vector3.up * 0.15f).normalized * PKnock.Value, ForceMode.VelocityChange);
                }
                catch { }
            }

            if (victim == null)
            {
                SpawnBall(h.point, new Color(1f, 0.9f, 0.5f), 0.08f, 0.25f, 0.1f);
                return;
            }

            string n = ht.name.ToLowerInvariant();
            bool head = n.Contains("head");
            float dmg = PDamage.Value * (head ? PHeadMult.Value : 1f);
            bool wasDead = victim.Dead;
            victim.Hp -= dmg;

            SpawnBlood(h.point, dir, head ? 12 : 7);
            if (head) SpawnBall(h.point, new Color(0.8f, 0.05f, 0.05f), 0.15f, 0.5f, 0.12f);

            if (PDismember.Value && !victim.Dead && rb != null && IsLimb(n))
            {
                int id = rb.GetInstanceID();
                int c;
                victim.LimbHits.TryGetValue(id, out c);
                c++;
                victim.LimbHits[id] = c;
                if (c >= Mathf.Max(1, PLimbHits.Value)) TearOff(rb, h.point, dir);
            }

            if (!wasDead && victim.Hp <= 0f) Kill(victim, h.point, dir);
        }

        private static bool IsLimb(string n)
        {
            if (n.Contains("head") || n.Contains("chest") || n.Contains("hips") || n.Contains("torso") || n.Contains("spine")) return false;
            return n.Contains("arm") || n.Contains("hand") || n.Contains("leg") || n.Contains("foot") || n.Contains("thigh") || n.Contains("calf") || n.Contains("knee") || n.Contains("elbow");
        }

        private void TearOff(Rigidbody rb, Vector3 point, Vector3 dir)
        {
            try
            {
                var j = rb.GetComponent<Joint>();
                if (j == null) return;
                UnityEngine.Object.Destroy(j);
                SpawnBlood(point, dir, 14);
                rb.AddForce((dir + Vector3.up * 0.5f).normalized * 9f, ForceMode.VelocityChange);
            }
            catch { }
        }

        private void Kill(Shooter v, Vector3 point, Vector3 dir)
        {
            v.Dead = true;
            v.Hp = 0f;
            LoggerInstance.Msg("Zabity: " + v.Actor.name);

            // postac przestaje sie sama podnosic
            try
            {
                var beh = v.Actor as Behaviour;
                if (beh != null) beh.enabled = false;
            }
            catch { }

            // rozwal wszystkie stawy i rozrzuc kawalki
            try
            {
                foreach (var o in FindAll(typeof(Joint)))
                {
                    var comp = o as Component;
                    if (comp == null || !comp.transform.IsChildOf(v.Top)) continue;
                    UnityEngine.Object.Destroy(comp);
                }
                foreach (var o in FindAll(typeof(Rigidbody)))
                {
                    var b = o as Rigidbody;
                    if (b == null || b.isKinematic || !b.transform.IsChildOf(v.Top)) continue;
                    Vector3 rnd = UnityEngine.Random.onUnitSphere;
                    Vector3 push = (dir * 0.6f + Vector3.up * 0.7f + rnd * 0.6f).normalized * UnityEngine.Random.Range(4f, 10f);
                    b.AddForce(push, ForceMode.VelocityChange);
                    b.AddTorque(UnityEngine.Random.insideUnitSphere * 8f, ForceMode.VelocityChange);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("Nie udalo sie rozwalic postaci: " + e.Message); }

            SpawnBlood(point, dir, 30);
            SpawnBall(point, new Color(0.7f, 0.02f, 0.02f), 0.3f, 1.4f, 0.25f);
            DropGun(v);
        }

        // ---------- Szukanie postaci ----------

        private void Scan()
        {
            try
            {
                if (!actorTypeSearched)
                {
                    actorTypeSearched = true;
                    actorType = FindActorType();
                    if (actorType != null)
                        LoggerInstance.Msg("Znaleziono typ postaci: " + actorType.FullName + " (" + actorType.Assembly.GetName().Name + ")");
                }
                if (actorType == null)
                {
                    if (!loggedNoActor)
                    {
                        loggedNoActor = true;
                        LoggerInstance.Warning("Nie znaleziono klasy 'Actor' - mod nie wie, gdzie sa postacie.");
                    }
                    return;
                }

                foreach (var o in FindAll(actorType))
                {
                    var comp = o as Component;
                    if (comp == null) continue;
                    int id = comp.GetInstanceID();
                    if (bad.Contains(id)) continue;
                    Shooter ex;
                    if (shooters.TryGetValue(id, out ex))
                    {
                        ex.LooksLocal = LooksLocal(comp);
                        continue;
                    }
                    Shooter s = CreateShooter(comp);
                    if (s != null) shooters[id] = s;
                    else bad.Add(id);
                }
            }
            catch (Exception e)
            {
                if (!scanErrLogged)
                {
                    scanErrLogged = true;
                    LoggerInstance.Error("Blad skanowania postaci (kolejne takie same pomijam): " + e);
                }
            }
        }

        private Shooter CreateShooter(Component actor)
        {
            Transform top = actor.transform;
            var s = new Shooter();
            s.Actor = actor;
            s.Top = top;
            s.Hips = FindBone(top, "actor_hips_collider", "hips");
            s.Chest = FindBone(top, "actor_chest_collider", "chest");
            s.Head = FindHead(top);
            s.Hand = FindBone(top, "actor_rightHand_collider", "righthand");
            if (s.Hips == null)
            {
                LoggerInstance.Warning("Postac '" + actor.name + "': brak bioder - pomijam.");
                return null;
            }
            if (s.Head != null)
            {
                var hc = s.Head.GetComponent<Collider>();
                if (hc != null)
                {
                    Vector3 sz = hc.bounds.size;
                    float d = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
                    if (d > 0.05f && d < 3f) s.Unit = d;
                }
            }
            s.Hp = PHealth.Value;
            s.Order = orderCounter++;
            s.LooksLocal = LooksLocal(actor);
            try { BuildGun(s); }
            catch (Exception e) { LoggerInstance.Warning("Nie udalo sie zrobic pistoletu: " + e.Message); }
            LoggerInstance.Msg("Uzbrojono: " + actor.name + " (reka=" + (s.Hand != null) + ", klatka=" + (s.Chest != null) + ", glowa=" + (s.Head != null) + ", lokalny=" + s.LooksLocal + ")");
            return s;
        }

        // Probuje zgadnac, ktora postac steruje ten komputer (bool "isLocal"/"isPlayer"/...). F5 to nadpisuje.
        private static bool LooksLocal(Component c)
        {
            try
            {
                Type t = c.GetType();
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.PropertyType != typeof(bool) || p.GetIndexParameters().Length > 0) continue;
                    string n = p.Name.ToLowerInvariant();
                    if (n.Contains("local") || n == "isplayer" || n.Contains("ishuman") || n.Contains("isuser"))
                    {
                        object v = p.GetValue(c, null);
                        if (v is bool && (bool)v) return true;
                    }
                }
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.FieldType != typeof(bool)) continue;
                    string n = f.Name.ToLowerInvariant();
                    if (n.Contains("local") || n == "isplayer" || n.Contains("ishuman") || n.Contains("isuser"))
                    {
                        object v = f.GetValue(c);
                        if (v is bool && (bool)v) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void RemoveInvalid()
        {
            List<int> rem = null;
            foreach (var kv in shooters)
            {
                if (kv.Value.Actor == null || kv.Value.Hips == null)
                {
                    if (rem == null) rem = new List<int>();
                    rem.Add(kv.Key);
                }
            }
            if (rem == null) return;
            foreach (int id in rem)
            {
                DestroyGun(shooters[id]);
                shooters.Remove(id);
            }
        }

        private static Transform FindBone(Transform start, string exact, string contains)
        {
            Transform cont = null;
            var q = new Queue<Transform>();
            q.Enqueue(start);
            int guard = 0;
            while (q.Count > 0 && guard++ < 600)
            {
                var t = q.Dequeue();
                if (t.name == exact) return t;
                if (cont == null && t.name.ToLowerInvariant().Contains(contains)) cont = t;
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
            }
            return cont;
        }

        private static Transform FindHead(Transform start)
        {
            Transform contains = null;
            var queue = new Queue<Transform>();
            queue.Enqueue(start);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 600)
            {
                var t = queue.Dequeue();
                string n = t.name.ToLowerInvariant();
                if (n == "head" || n == "actor_head_collider") return t;
                if (contains == null && n.Contains("head") && !n.Contains("phone") && !n.Contains("set")) contains = t;
                for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
            }
            return contains;
        }

        private static Type FindActorType()
        {
            Type best = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (n.StartsWith("UnityEngine") || n.StartsWith("System") || n.StartsWith("mscorlib")
                    || n.StartsWith("MelonLoader") || n.StartsWith("Mono.") || n.StartsWith("0Harmony")
                    || n.StartsWith("Il2CppInterop") || n.StartsWith("Il2Cppmscorlib") || n.StartsWith("Il2CppSystem"))
                    continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null || t.Name != "Actor") continue;
                    bool isComponent;
                    try { isComponent = typeof(Component).IsAssignableFrom(t); } catch { continue; }
                    if (!isComponent) continue;
                    if (n.Contains("Assembly-CSharp")) return t;
                    if (best == null) best = t;
                }
            }
            return best;
        }

        // ---------- Pistolet (z szescianow) ----------

        private void BuildGun(Shooter s)
        {
            var root = new GameObject("GBGunsPistol");
            AddPart(root.transform, new Vector3(0f, 0f, 0.18f), new Vector3(0.09f, 0.11f, 0.42f), Vector3.zero, new Color(0.12f, 0.12f, 0.14f));
            AddPart(root.transform, new Vector3(0f, -0.13f, 0.05f), new Vector3(0.08f, 0.2f, 0.1f), new Vector3(-12f, 0f, 0f), new Color(0.35f, 0.2f, 0.1f));
            AddPart(root.transform, new Vector3(0f, 0.01f, 0.4f), new Vector3(0.05f, 0.05f, 0.1f), Vector3.zero, new Color(0.55f, 0.55f, 0.6f));
            float scale = s.Unit / 0.4f * PGunSize.Value;
            root.transform.localScale = Vector3.one * scale;
            s.Gun = root;
            s.GunT = root.transform;
            PlaceGun(s);
        }

        private static void AddPart(Transform parent, Vector3 pos, Vector3 size, Vector3 euler, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.transform.localRotation = Quaternion.Euler(euler);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                Colorize(r, c);
            }
        }

        private void PlaceGun(Shooter s)
        {
            if (s.GunT == null) return;
            float scale = s.Unit / 0.4f * PGunSize.Value;
            Vector3 basePos;
            if (s.Hand != null) basePos = s.Hand.position;
            else basePos = s.Hips.position + Vector3.up * (0.2f * scale);
            Vector3 a = s.Aim.sqrMagnitude > 0.0001f ? s.Aim : Vector3.forward;
            s.GunT.position = basePos + a * (0.12f * scale);
            s.GunT.rotation = Quaternion.LookRotation(a, Vector3.up);
        }

        private static void DestroyGun(Shooter s)
        {
            if (s.Gun != null) { try { UnityEngine.Object.Destroy(s.Gun); } catch { } }
            s.Gun = null;
            s.GunT = null;
        }

        // Po smierci pistolet wypada na ziemie
        private static void DropGun(Shooter s)
        {
            if (s.Gun == null) return;
            try
            {
                var rb = s.Gun.AddComponent<Rigidbody>();
                var bc = s.Gun.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, -0.04f, 0.18f);
                bc.size = new Vector3(0.1f, 0.3f, 0.45f);
                rb.AddForce(UnityEngine.Random.onUnitSphere * 2f, ForceMode.VelocityChange);
                UnityEngine.Object.Destroy(s.Gun, 8f);
            }
            catch
            {
                try { UnityEngine.Object.Destroy(s.Gun, 1f); } catch { }
            }
            s.Gun = null;
            s.GunT = null;
        }

        // ---------- Efekty ----------

        private static void Colorize(Renderer r, Color c)
        {
            // Gra uzywa URP - sprawdzony w innych modach "Sprites/Default" sie renderuje
            if (!shaderSearched)
            {
                shaderSearched = true;
                string[] names = { "Sprites/Default", "Unlit/Color", "Universal Render Pipeline/Unlit", "Hidden/Internal-Colored" };
                foreach (var n in names)
                {
                    Shader sh = null;
                    try { sh = Shader.Find(n); } catch { }
                    if (sh != null) { cachedShader = sh; break; }
                }
                MelonLogger.Msg("[Guns] shader: " + (cachedShader != null ? cachedShader.name : "(brak - domyslny material)"));
            }
            try
            {
                if (cachedShader != null)
                {
                    var m = new Material(cachedShader);
                    m.color = c;
                    r.sharedMaterial = m;
                    return;
                }
                r.material.color = c;
            }
            catch (Exception e) { MelonLogger.Warning("[Guns] Colorize: " + e.Message); }
        }

        private static GameObject MakePrim(PrimitiveType type, string name, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                Colorize(r, color);
            }
            return go;
        }

        private void SpawnBall(Vector3 pos, Color color, float s0, float s1, float life)
        {
            try
            {
                var go = MakePrim(PrimitiveType.Sphere, "GBGunsFx", color);
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * s0;
                fx.Add(new FxItem { Go = go, T = go.transform, Born = Time.time, Life = life, S0 = s0, S1 = s1, Kind = 0 });
            }
            catch { }
        }

        private void SpawnTracer(Vector3 a, Vector3 b)
        {
            try
            {
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 0.05f) return;
                var go = MakePrim(PrimitiveType.Cube, "GBGunsTracer", new Color(1f, 0.95f, 0.5f));
                go.transform.position = (a + b) * 0.5f;
                go.transform.rotation = Quaternion.LookRotation(d / len, Vector3.up);
                go.transform.localScale = new Vector3(0.04f, 0.04f, len);
                fx.Add(new FxItem { Go = go, T = go.transform, Born = Time.time, Life = 0.07f, S0 = len, Kind = 1, Thick = 0.04f });
            }
            catch { }
        }

        private void SpawnBlood(Vector3 pos, Vector3 dir, int count)
        {
            try
            {
                Color[] cols = { new Color(0.75f, 0.03f, 0.03f), new Color(0.55f, 0.02f, 0.02f), new Color(0.9f, 0.1f, 0.08f) };
                for (int i = 0; i < count; i++)
                {
                    var go = MakePrim(PrimitiveType.Sphere, "GBGunsBlood", cols[UnityEngine.Random.Range(0, cols.Length)]);
                    go.transform.position = pos;
                    go.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.05f, 0.14f);
                    Vector3 v = (dir * 0.5f + UnityEngine.Random.onUnitSphere * 0.9f + Vector3.up * 0.4f).normalized * UnityEngine.Random.Range(2f, 6f);
                    debris.Add(new Debris { T = go.transform, V = v, Die = Time.time + UnityEngine.Random.Range(0.5f, 1.1f) });
                }
            }
            catch { }
        }

        private void SetDot(bool on, Vector3 pos)
        {
            try
            {
                if (on)
                {
                    if (dot == null) dot = MakePrim(PrimitiveType.Sphere, "GBGunsDot", new Color(1f, 0.1f, 0.1f));
                    dot.transform.position = pos + Vector3.up * 0.05f;
                    dot.transform.localScale = Vector3.one * 0.18f;
                    if (!dotVisible) { dot.SetActive(true); dotVisible = true; }
                }
                else if (dot != null && dotVisible)
                {
                    dot.SetActive(false);
                    dotVisible = false;
                }
            }
            catch { }
        }

        private void TickFx(float dt)
        {
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var it = fx[i];
                if (it.Go == null) { fx.RemoveAt(i); continue; }
                float p = (Time.time - it.Born) / it.Life;
                if (p >= 1f)
                {
                    UnityEngine.Object.Destroy(it.Go);
                    fx.RemoveAt(i);
                    continue;
                }
                if (it.Kind == 1)
                {
                    float th = it.Thick * (1f - p);
                    it.T.localScale = new Vector3(th, th, it.S0);
                }
                else
                {
                    float s = Mathf.Lerp(it.S0, it.S1, p) * (1f - p * 0.6f);
                    it.T.localScale = Vector3.one * Mathf.Max(0.001f, s);
                }
            }

            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i];
                if (d.T == null || Time.time > d.Die)
                {
                    if (d.T != null) UnityEngine.Object.Destroy(d.T.gameObject);
                    debris.RemoveAt(i);
                    continue;
                }
                d.V += Physics.gravity * dt;
                d.T.position += d.V * dt;
            }
        }

        private void ClearFx()
        {
            foreach (var it in fx)
                if (it.Go != null) { try { UnityEngine.Object.Destroy(it.Go); } catch { } }
            fx.Clear();
            foreach (var d in debris)
                if (d.T != null) { try { UnityEngine.Object.Destroy(d.T.gameObject); } catch { } }
            debris.Clear();
        }

        // ---------- Dzwiek strzalu (generowany, bez plikow) ----------

        private void PlayShot(Vector3 pos)
        {
            try
            {
                if (shotClip == null && !shotTried)
                {
                    shotTried = true;
                    shotClip = BuildShot();
                    if (shotClip == null) LoggerInstance.Warning("Dzwiek strzalu niedostepny w tej wersji gry (mod dziala bez dzwieku).");
                }
                if (shotClip != null) AudioSource.PlayClipAtPoint(shotClip, pos, Mathf.Clamp01(PVolume.Value));
            }
            catch { }
        }

        private static AudioClip BuildShot()
        {
            try
            {
                int rate = 22050;
                int n = (int)(rate * 0.22f);
                float[] data = new float[n];
                var rnd = new System.Random(4321);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Exp(-t * 26f);
                    float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    lp += (noise - lp) * Mathf.Lerp(0.8f, 0.2f, Mathf.Clamp01(t * 6f));
                    float thump = Mathf.Sin(2f * Mathf.PI * (180f - 120f * t) * t);
                    data[i] = Mathf.Clamp((lp * 1.4f + thump * 0.8f) * env, -1f, 1f);
                }
                AudioClip clip = AudioClip.Create("gbguns_shot", n, 1, rate, false);
                foreach (var m in typeof(AudioClip).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "SetData") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2 || ps[1].ParameterType != typeof(int)) continue;
                    object arg = ps[0].ParameterType == typeof(float[])
                        ? (object)data
                        : Activator.CreateInstance(ps[0].ParameterType, new object[] { data });
                    m.Invoke(clip, new object[] { arg, 0 });
                    return clip;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Guns] nie udalo sie zrobic dzwieku: " + e.Message);
            }
            return null;
        }

        // ---------- Kamera ----------

        private Camera GetCam()
        {
            if (cam != null && cam.enabled && cam.gameObject.activeInHierarchy) return cam;
            cam = null;
            camTimer -= Time.unscaledDeltaTime;
            if (camTimer > 0f) return null;
            camTimer = 0.5f;

            Camera m = Camera.main;
            if (m != null && m.enabled) { cam = m; return cam; }

            Camera best = null;
            foreach (var o in FindAll(typeof(Camera)))
            {
                var c = o as Camera;
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            cam = best;
            return cam;
        }

        // ---------- FindObjectsOfType<T>() przez refleksje (w IL2CPP zwraca tablice Il2Cpp) ----------

        private static List<UnityEngine.Object> FindAll(Type t)
        {
            var result = new List<UnityEngine.Object>();
            if (!findObjectsResolved)
            {
                findObjectsResolved = true;
                MethodInfo noArgs = null, boolArg = null;
                foreach (var m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "FindObjectsOfType" || !m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) noArgs = m;
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) boolArg = m;
                }
                findObjectsMethod = noArgs ?? boolArg;
                findObjectsNeedsBool = noArgs == null && boolArg != null;
            }
            if (findObjectsMethod == null) return result;

            var gm = findObjectsMethod.MakeGenericMethod(t);
            object res = gm.Invoke(null, findObjectsNeedsBool ? new object[] { false } : null);
            var en = res as IEnumerable;
            if (en == null) return result;
            foreach (var o in en)
            {
                var u = o as UnityEngine.Object;
                if (u != null) result.Add(u);
            }
            return result;
        }
    }
}
