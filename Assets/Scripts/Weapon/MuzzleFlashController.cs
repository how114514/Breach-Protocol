using System.Collections;
using UnityEngine;

/// <summary>
/// 控制一次开火的枪口闪光（粒子 + 灯光）。
/// 替代 VFX 自带无限循环的 WFX_LightFlicker：
/// 不自动播放、不无限循环，一切由外部调用 PlayFlash() 触发。
/// </summary>
public class MuzzleFlashController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ParticleSystem muzzleFlashParticle; // 枪口火光粒子
    [SerializeField] private Light flashLight;                   // 枪口闪光灯光
    [SerializeField] private float lightDuration = 0.1f;         // 灯光持续时长，超时自动关闭

    private Coroutine lightRoutine; // 关灯协程引用，连续开火时先取消上一次的

    private void Awake()
    {
        // 初始化：关闭灯光，粒子不播放（完全由 PlayFlash 触发，启动时枪口保持熄灭）
        if (flashLight != null)
            flashLight.enabled = false;

        if (muzzleFlashParticle != null && muzzleFlashParticle.isPlaying)
            muzzleFlashParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    /// <summary>
    /// 触发一次开火闪光：重播粒子 + 开启灯光，lightDuration 秒后自动关闭灯光。
    /// 连续快速开火时会取消上一次的关灯计时，从本轮重新开始。
    /// </summary>
    public void PlayFlash()
    {
        // 连续快速开火支持：先取消上一次的关灯协程，灯光从本轮重新计时
        if (lightRoutine != null)
        {
            StopCoroutine(lightRoutine);
            lightRoutine = null;
        }

        // 重播粒子（先 Stop 再 Play，保证每次开火都从头闪烁）
        if (muzzleFlashParticle != null)
        {
            muzzleFlashParticle.Stop();
            muzzleFlashParticle.Play();
        }

        // 开启灯光
        if (flashLight != null)
            flashLight.enabled = true;

        // 定时自动关灯
        lightRoutine = StartCoroutine(TurnOffLightAfterDelay());
    }

    private IEnumerator TurnOffLightAfterDelay()
    {
        yield return new WaitForSeconds(lightDuration);

        if (flashLight != null)
            flashLight.enabled = false;
        lightRoutine = null;
    }
}
