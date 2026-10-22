using System;
using System.Collections.Generic;
using UnityEngine;

namespace HubRaceway
{
    // Effects for the power-ups and pads, so you can see what just happened: rings that spread out, bits that fly off, glowing
    // trails behind a car, a bubble round a car, and the game's own speed-gate burst. Each lives a moment and cleans itself up;
    // the raceway ticks them every frame and clears them all when it goes.
    internal sealed class Fx
    {
        private sealed class Live
        {
            public GameObject Go;
            public float Born, Life;
            public Action<Live, float> Tick;   // t: 0 to 1 through its life
        }

        private readonly Builder builder;
        private readonly Transform root;
        private readonly List<Live> live = new List<Live>();
        private Mesh ring, thinRing;

        public Fx(Builder builder, Transform root)
        {
            this.builder = builder;
            this.root = root;
        }

        private Mesh RingMesh => ring != null ? ring : ring = builder.Torus(1f, 0.06f, 64, 8);
        private Mesh ThinRingMesh => thinRing != null ? thinRing : thinRing = builder.Torus(1f, 0.03f, 48, 6, arcs: 6, fill: 0.7f);

        private Live Add(GameObject go, float life, Action<Live, float> tick)
        {
            go.transform.SetParent(root, true);
            var l = new Live { Go = go, Born = Time.time, Life = life, Tick = tick };
            live.Add(l);
            tick(l, 0f);
            return l;
        }

        private GameObject MeshObject(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // A ring spreading out from a point, flat to `normal`, from `from` to `to` metres across, thinning as it goes.
        public void Wave(Vector3 at, Vector3 normal, Material material, float from, float to, float seconds)
        {
            var go = MeshObject("FxWave", RingMesh, material);
            go.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.forward, normal));
            Add(go, seconds, (l, t) =>
            {
                float e = 1f - (1f - t) * (1f - t);
                float r = Mathf.Lerp(from, to, e);
                float thick = Mathf.Lerp(1f, 0.15f, t) * Mathf.Max(1f, r * 0.08f);
                l.Go.transform.localScale = new Vector3(r, r, thick * 6f);
            });
        }

        // Bits that fly out from a point and shrink away.
        public void Debris(Vector3 at, Material material, int count, float speed, float size)
        {
            for (int i = 0; i < count; i++)
            {
                var go = builder.Copy(PrimitiveType.Cube, "FxBit");
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
                go.transform.position = at;
                Vector3 v = UnityEngine.Random.onUnitSphere * speed * UnityEngine.Random.Range(0.5f, 1f);
                Vector3 spin = UnityEngine.Random.onUnitSphere * 720f;
                float s = size * UnityEngine.Random.Range(0.5f, 1.2f);
                Vector3 start = at;
                Add(go, 0.9f, (l, t) =>
                {
                    float age = t * l.Life;
                    l.Go.transform.position = start + v * age + Physics.gravity * (0.5f * age * age);
                    l.Go.transform.rotation = Quaternion.Euler(spin * age);
                    l.Go.transform.localScale = Vector3.one * s * (1f - t);
                });
            }
        }

        // Glowing streaks from the back of a car for a while.
        public void Trails(Transform car, Material material, float seconds, float width)
        {
            foreach (float side in new[] { -1.1f, 1.1f })
            {
                var go = new GameObject("FxTrail");
                go.transform.SetParent(car, false);
                go.transform.localPosition = new Vector3(side, 0.6f, -2.2f);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.45f;
                trail.startWidth = width;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.3f;
                trail.sharedMaterial = material;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var l = new Live { Go = go, Born = Time.time, Life = seconds, Tick = (x, t) => { } };
                live.Add(l);
            }
        }

        // Rings spinning round a car (a shield, or crackling when it's shocked).
        public void Bubble(Transform car, Material material, float seconds, float radius)
        {
            for (int k = 0; k < 3; k++)
            {
                var go = MeshObject("FxBubble", ThinRingMesh, material);
                go.transform.SetParent(car, false);
                go.transform.localPosition = Vector3.up * 0.8f;
                go.transform.localScale = Vector3.one * radius;
                Quaternion axis = Quaternion.Euler(k * 60f, k * 45f, 0f);
                float speed = 160f + k * 70f;
                var l = new Live { Go = go, Born = Time.time, Life = seconds };
                l.Tick = (x, t) =>
                {
                    x.Go.transform.localRotation = axis * Quaternion.Euler(0f, (Time.time - x.Born) * speed, 0f) * Quaternion.Euler(90f, 0f, 0f);
                    float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 18f + k);
                    float fade = t > 0.85f ? (1f - t) / 0.15f : 1f;
                    x.Go.transform.localScale = Vector3.one * radius * pulse * Mathf.Max(fade, 0.05f);
                };
                live.Add(l);
            }
        }

        // The game's speed-gate flash.
        public void Burst(Vector3 at, Quaternion rotation, float size)
        {
            try { BurstEffect.InitializeObjectPosition(Global.Instance.SpeedGateEffect, at, rotation, size); } catch (Exception) { }
        }

        public void Tick()
        {
            float now = Time.time;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Live l = live[i];
                float t = (now - l.Born) / l.Life;
                if (l.Go == null || t >= 1f)
                {
                    if (l.Go != null) UnityEngine.Object.Destroy(l.Go);
                    live.RemoveAt(i);
                    continue;
                }
                l.Tick?.Invoke(l, t);
            }
        }

        public void Clear()
        {
            foreach (Live l in live) if (l.Go != null) UnityEngine.Object.Destroy(l.Go);
            live.Clear();
        }
    }
}
