using UnityEngine;

namespace Aprender
{
    /// <summary>Small additive motion also works with the unrigged Kenney Cube Pets.</summary>
    public sealed class AvatarMotion : MonoBehaviour
    {
        public Transform Visual { get; set; }
        public bool IsRemote { get; set; }
        public Vector3 TargetPosition { get; set; }
        public float TargetYaw { get; set; }
        private Vector3 previousPosition;
        private float gait;
        private float waveUntil;
        private float motionAmount;
        private Animator animator;
        private Animation legacyAnimation;

        public void Setup(Transform visual)
        {
            Visual = visual;
            previousPosition = transform.position;
            animator = visual.GetComponentInChildren<Animator>();
            legacyAnimation = visual.GetComponentInChildren<Animation>();
            if (legacyAnimation != null && legacyAnimation.clip != null)
            {
                legacyAnimation.wrapMode = WrapMode.Loop;
                legacyAnimation.Play();
            }
        }

        public void Wave() => waveUntil = Time.time + 1.5f;

        private void Update()
        {
            if (IsRemote)
            {
                var blend = 1f - Mathf.Exp(-12f * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, TargetPosition, blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, TargetYaw, 0), blend);
            }

            if (Visual == null) return;
            var distance = Vector3.Distance(transform.position, previousPosition);
            previousPosition = transform.position;
            var moving = distance > .003f;
            motionAmount = Mathf.MoveTowards(motionAmount, moving ? 1 : 0, Time.deltaTime * 7f);
            gait += Time.deltaTime * (moving ? 12f : 2f);
            var waving = Time.time < waveUntil;
            var bob = Mathf.Abs(Mathf.Sin(gait)) * .10f * motionAmount;
            var sway = Mathf.Sin(gait) * 5f * motionAmount;
            if (waving) bob += Mathf.Abs(Mathf.Sin(Time.time * 12f)) * .17f;
            Visual.localPosition = new Vector3(0, bob, 0);
            Visual.localRotation = Quaternion.Euler(0, waving ? Mathf.Sin(Time.time * 13f) * 20f : 0,
                waving ? Mathf.Sin(Time.time * 13f) * 10f : sway);

            if (animator != null && animator.runtimeAnimatorController != null)
                animator.speed = moving ? 1.2f : .65f;
        }
    }
}
