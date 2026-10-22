using System.Collections.Generic;
using Pigeon.Movement;
using UnityEngine;

namespace HubRaceway
{
    // What the raceway does to the car you're driving: holds it to the tube's walls and ceiling (pulled toward the nearest wall,
    // not down), the power-ups' effects (turbo, slowed, spun out, shielded), and holds it on its grid slot until the lights go green. It only
    // ever touches your own car (the game lets the driver move it), and puts everything back when it's done.
    internal sealed class Car
    {
        // pulled toward the tube's nearest wall this hard (a little more than the car's own gravity, so it sticks upside down)
        private const float MagnetGravity = 26f;
        private const float TurboTopSpeed = 26f, TurboPush = 22f;

        public float TurboUntil, SlowUntil, SpinUntil, ShieldUntil;
        public bool Frozen;
        public Vector3 FreezeAt;
        public Quaternion FreezeRotation;

        private Vehicle car;
        private float baseMaxSpeed;
        private bool magnetOn;
        // the cars whose top speed we changed, to put back
        private readonly Dictionary<Vehicle, float> touched = new Dictionary<Vehicle, float>();

        public static Vehicle Mine()
        {
            Player p = Player.LocalPlayer;
            if (p == null) return null;
            Vehicle v = p.CurrentVehicle;
            return v != null && v.IsLocalPlayerDriving && v.IsOwner ? v : null;
        }

        public bool Shielded => Time.time < ShieldUntil;

        public void Turbo(float seconds)
        {
            TurboUntil = Mathf.Max(TurboUntil, Time.time + seconds);
            Vehicle v = Mine();
            if (v != null)
            {
                v.Rigidbody.AddForce(v.transform.forward * 12f, ForceMode.VelocityChange);
                try { BurstEffect.InitializeObjectPosition(Global.Instance.SpeedGateEffect, v.transform.position, v.transform.rotation, 0.5f); } catch (System.Exception) { }
            }
            Sound.Play("Play_Speedgate_Boost");
        }

        public void Slow(float seconds)
        {
            if (Shielded) return;
            SlowUntil = Mathf.Max(SlowUntil, Time.time + seconds);
        }

        public void Spin()
        {
            if (Shielded) return;
            SpinUntil = Time.time + 0.9f;
            Vehicle v = Mine();
            if (v != null) v.Rigidbody.linearVelocity *= 0.3f;
        }

        public void Shield(float seconds)
        {
            ShieldUntil = Mathf.Max(ShieldUntil, Time.time + seconds);
            SlowUntil = SpinUntil = 0f;
        }

        public void Freeze(Vector3 at, Quaternion rotation)
        {
            Frozen = true;
            FreezeAt = at;
            FreezeRotation = rotation;
        }

        // Every physics step, after the car's own (it runs earlier in the frame).
        public void FixedTick(Track track, int sample, bool onRoad)
        {
            Vehicle v = Mine();
            if (v != car)
            {
                Release();
                car = v;
                if (car != null)
                {
                    if (!touched.TryGetValue(car, out baseMaxSpeed)) touched[car] = baseMaxSpeed = car.maxSpeed;
                }
            }
            if (car == null) return;
            Rigidbody body = car.Rigidbody;
            float now = Time.time;

            if (Frozen)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = FreezeAt;
                body.rotation = FreezeRotation;
                return;
            }

            // the top speed: up while on turbo, down while slowed
            float max = baseMaxSpeed;
            if (now < TurboUntil || Shielded) max += TurboTopSpeed;
            if (now < SlowUntil) max *= 0.5f;
            car.maxSpeed = max;
            if (now < TurboUntil || Shielded)
            {
                if (Vector3.Dot(body.linearVelocity, car.transform.forward) < max) body.AddForce(car.transform.forward * TurboPush, ForceMode.Acceleration);
            }
            if (now < SlowUntil) body.AddForce(-body.linearVelocity * 1.2f, ForceMode.Acceleration);
            if (now < SpinUntil) body.angularVelocity = car.transform.up * 9f;

            // in the tube: the world's pull (and the hover car's own extra pull down) swapped for one toward the nearest wall, so
            // you can drive up the sides and over the ceiling
            bool magnet = onRoad && sample >= 0 && track.InTunnel(sample);
            if (magnet)
            {
                body.useGravity = false;
                Vector3 l = track.Local(sample, body.position);
                Vector2 o = track.TubeOut(sample, new Vector2(l.x, l.y));
                Vector3 pull = (track.Right[sample] * o.x + track.Up[sample] * o.y) * MagnetGravity;
                if (car is Blitseg b) pull -= Vector3.up * b.data.addGravity;
                body.AddForce(pull, ForceMode.Acceleration);
                magnetOn = true;
            }
            else if (magnetOn)
            {
                body.useGravity = true;
                magnetOn = false;
            }
        }

        // Puts the car back as the game had it.
        public void Release()
        {
            if (car != null)
            {
                if (touched.TryGetValue(car, out float m)) car.maxSpeed = m;
                if (magnetOn) car.Rigidbody.useGravity = true;
            }
            magnetOn = false;
            car = null;
        }

        public void ReleaseAll()
        {
            Release();
            foreach (var kv in touched) if (kv.Key != null) kv.Key.maxSpeed = kv.Value;
            touched.Clear();
        }
    }

    internal static class Sound
    {
        public static void Play(string evt)
        {
            try { if (PlayerLook.Instance != null) AkUnitySoundEngine.PostEvent(evt, PlayerLook.Instance.gameObject); }
            catch (System.Exception) { }
        }
    }
}
