using UnityEngine;

namespace AIT.Riding
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class RideEngineAudio : MonoBehaviour
    {
        public RideTestSession session;
        AudioClip carrier;
        volatile float frequency, volume;
        double phase;
        int sampleRate;

        void Start()
        {
            sampleRate = AudioSettings.outputSampleRate;
            carrier = AudioClip.Create("Synthetic scooter cue (not a recorded engine)", sampleRate, 1, sampleRate, false);
            // A silent carrier keeps the audio source/DSP callback active.
            var source = GetComponent<AudioSource>();
            source.clip = carrier; source.loop = true; source.spatialBlend = 0; source.Play();
        }
        void Update()
        {
            frequency = 35 + Mathf.Abs(session.Model.speed) * 4 + session.CurrentInput.throttle * 12;
            volume = session.Running && session.Settings.engineSoundEnabled ?
                0.025f + session.CurrentInput.throttle * 0.025f : 0;
        }
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (sampleRate <= 0) return;
            float f = frequency, gain = volume;
            for (int i = 0; i < data.Length; i += channels)
            {
                phase += 2 * System.Math.PI * f / sampleRate;
                if (phase > 2 * System.Math.PI) phase -= 2 * System.Math.PI;
                float value = gain * (float)(System.Math.Sin(phase) + 0.3 * System.Math.Sin(phase * 2)
                    + 0.15 * System.Math.Sin(phase * 3));
                for (int c = 0; c < channels; ++c) data[i + c] = value;
            }
        }
        void OnDestroy() { if (carrier) Destroy(carrier); }
    }
}
