using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// A snowball in flight. It travels in a straight line along the direction it was fired,
    /// and reports the exact contact point so the hit can be scored as head or body.
    /// </summary>
    public sealed class Snowball : MonoBehaviour
    {
        Vector3 velocity;
        float life;
        bool dead;
        SnowCannonGame game;

        // Random tumble: each snowball gets its own spin axis and rate at launch so the
        // speckled surface catches the light differently and the shot reads as a real packed
        // snowball rather than a silently sliding sphere.
        Vector3 spinAxis;
        float spinSpeed;

        // The shot has to out-reach the snowmen dealt to the far side of a wide screen. The
        // limiting factor is not the speed but the heavy arc: at the configured gravity a
        // low-aimed ball touches down in front of a distant target and despawns on the snow.
        // These are the values the ball actually flies with, and the aim assist reads them too
        // (see SnowCannonGame's BallisticDir call) so the solved arc matches the flight.
        public static readonly float LaunchSpeed = GameConfig.SnowballSpeed;
        public static readonly float Gravity = GameConfig.SnowballGravity / 1.5f;
        public static readonly float LifeTime = GameConfig.SnowballLifeTime * 1.6f;

        // Highest point a shot can physically reach, v²/2g above the muzzle, plus headroom.
        // The despawn test used a fixed ceiling of 24, which sits well below this, so every
        // shot aimed above roughly 33 degrees was deleted at the top of its own arc and
        // vanished in mid-air -- exactly the "ball disappears before it reaches the snowman"
        // complaint. Deriving the bound from the launch values keeps mortar shots alive and
        // re-tunes itself automatically if the speed or gravity is ever adjusted.
        public static readonly float MaxHeight = LaunchSpeed * LaunchSpeed / (2f * Gravity) + 6f;

        // Horizontal despawn bound, likewise generous versus the logical field width (see Update).
        const float DespawnHalfWidth = 40f;

        public static Snowball Fire(Vector3 origin, Vector3 direction, SnowCannonGame owner)
        {
            // A primitive sphere ships with a mesh + sphere collider already attached, so we
            // reuse those (the built-in extra-mesh API is gone in 6.6).
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Snowball";
            go.transform.position = origin;
            var sb = go.AddComponent<Snowball>();
            sb.game = owner;
            sb.velocity = direction.normalized * LaunchSpeed;
            sb.life = LifeTime;

            // onUnitSphere is already normalised, so no extra divide; the rate is randomised
            // per shot so a volley never tumbles in lockstep.
            sb.spinAxis = Random.onUnitSphere;
            sb.spinSpeed = Random.Range(180f, 540f);

            var r = go.GetComponent<MeshRenderer>();
            r.material = Mat.Textured(TextureFactory.SnowballSnow(), GameConfig.SnowWhite);
            // A lumpy, hand-packed shape instead of a perfect sphere; the collider stays a
            // unit sphere so hit detection is unchanged.
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = MeshFactory.LumpySnowball(Random.Range(0, 100000));
            // Unit sphere (local radius 0.5): scale so the world radius matches SnowballRadius.
            go.transform.localScale = Vector3.one * (GameConfig.SnowballRadius * 2f);

            var col = go.GetComponent<SphereCollider>();
            col.radius = 0.5f;
            col.isTrigger = true;

            return sb;
        }

        void Update()
        {
            if (dead) return;

            life -= Time.deltaTime;
            if (life <= 0f)
            {
                Despawn();
                return;
            }

            Vector3 move = velocity * Time.deltaTime;
            float step = move.magnitude;
            if (step <= 0f) { Despawn(); return; }

            // Ballistic: gravity bends the flight into an arc, so a too-flat shot sails over a
            // snowman and a too-steep one drops short. Applied after this frame's cast.
            velocity.y -= Gravity * Time.deltaTime;

            // A sphere cast along this frame's motion, so fast snowballs never tunnel through
            // a snowman between two frames. (6.6 orders the out-hit before the max distance.)
            RaycastHit hit;
            if (Physics.SphereCast(new Ray(transform.position, move.normalized),
                                   GameConfig.SnowballRadius * 0.85f, out hit, step))
            {
                if (hit.transform != null)
                {
                    var t = hit.transform.GetComponentInParent<IHitTarget>();
                    if (t != null && t.Alive)
                    {
                        OnHit(t, hit.point);
                        return;
                    }
                }
            }

            transform.position += move;

            // Tumble about the launch-time random axis. Rotation is independent of the
            // position integration above, and the collider is a sphere, so the spin never
            // affects the trajectory or hit detection.
            if (spinSpeed > 0f) transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);

            // Out of the play field entirely, or dropped onto the snow. The x bound is generous
            // (not the narrow logical FieldHalfWidth) because snowmen are dealt across the full
            // VISIBLE width, which on a wide screen reaches well past FieldHalfWidth; killing the
            // ball at the logical width made far-left/far-right snowmen unreachable. The y floor
            // allows a little below ground so a low arc can still clip a snowman's base, and the
            // ceiling is derived from the launch physics so a high arc is never culled at its apex.
            var p = transform.position;
            if (p.z > GameConfig.FieldMaxZ + 6f || p.z < GameConfig.FieldMinZ - 6f ||
                Mathf.Abs(p.x) > DespawnHalfWidth || p.y > MaxHeight || p.y < -0.6f)
            {
                Despawn();
            }
        }

        void OnHit(IHitTarget target, Vector3 point)
        {
            int points;
            bool counted = target.Hit(point, out points);
            if (counted && game != null) game.RegisterHit(target, points);
            if (game != null) game.PlayPopAt(point);
            Despawn();
        }

        void Despawn()
        {
            if (dead) return;
            dead = true;
            Destroy(gameObject);
        }
    }
}
