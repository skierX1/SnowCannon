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

        public static Snowball Fire(Vector3 origin, Vector3 direction, SnowCannonGame owner)
        {
            // A primitive sphere ships with a mesh + sphere collider already attached, so we
            // reuse those (the built-in extra-mesh API is gone in 6.6).
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Snowball";
            go.transform.position = origin;
            var sb = go.AddComponent<Snowball>();
            sb.game = owner;
            sb.velocity = direction.normalized * GameConfig.SnowballSpeed;
            sb.life = GameConfig.SnowballLifeTime;

            var r = go.GetComponent<MeshRenderer>();
            r.material = Mat.Textured(TextureFactory.Snow(), GameConfig.SnowWhite);
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
            velocity.y -= GameConfig.SnowballGravity * Time.deltaTime;

            // A sphere cast along this frame's motion, so fast snowballs never tunnel through
            // a snowman between two frames. (6.6 orders the out-hit before the max distance.)
            RaycastHit hit;
            if (Physics.SphereCast(new Ray(transform.position, move.normalized),
                                   GameConfig.SnowballRadius * 0.85f, out hit, step))
            {
                if (hit.transform != null)
                {
                    var sm = hit.transform.GetComponentInParent<Snowman>();
                    if (sm != null)
                    {
                        OnHit(sm, hit.point);
                        return;
                    }
                }
            }

            transform.position += move;

            // Out of the play field entirely, or dropped onto the snow.
            var p = transform.position;
            if (p.z > GameConfig.FieldMaxZ + 6f || p.z < GameConfig.FieldMinZ - 6f ||
                Mathf.Abs(p.x) > GameConfig.FieldHalfWidth + 4f || p.y > 24f || p.y < 0f)
            {
                Despawn();
            }
        }

        void OnHit(Snowman target, Vector3 point)
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
