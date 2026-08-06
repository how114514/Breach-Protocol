using UnityEngine;

/// <summary>
/// 战斗状态：确认玩家为目标，开启瞄准与射击，并围绕玩家左右横向移动。
/// 瞄准（EnemyAimController）与射击（EnemyShootController）是独立组件，开启后各自按逻辑运作，
/// 本状态只负责“是否处于战斗”的判定，不直接操作枪械旋转与子弹生成。
/// 持续检测玩家是否丢失：离开警戒范围或视线被遮挡累计 lostTargetTime 秒 → 回到巡逻。
/// </summary>
public class EnemyCombatState : IEnemyState
{
    private float lostTargetTimer;      // 丢失玩家累计时间（秒）
    private float strafeTimer;          // 换向计时（秒）
    private float strafeDirection = 1f; // 当前横向移动方向：-1 左 / 1 右

    public void Enter(EnemyStateMachine stateMachine)
    {
        // 进入战斗：开启瞄准与射击，由各自组件处理旋转与开火时机
        if (stateMachine.AimController != null)
            stateMachine.AimController.enabled = true;
        if (stateMachine.ShootController != null)
            stateMachine.ShootController.enabled = true;

        lostTargetTimer = 0f;
        strafeTimer = 0f;
    }

    public void Update(EnemyStateMachine stateMachine)
    {
        // 玩家仍在警戒范围内且视线无遮挡 → 继续战斗，重置丢失计时，并横向走位
        if (stateMachine.IsPlayerInRangeAndVisible())
        {
            lostTargetTimer = 0f;
            UpdateStrafe(stateMachine);
            return;
        }

        // 玩家丢失（离开范围或视线被遮挡）→ 累计计时，超过阈值后回到巡逻
        lostTargetTimer += Time.deltaTime;
        if (lostTargetTimer > stateMachine.LostTargetTime)
            stateMachine.SwitchState(stateMachine.PatrolState);
    }

    public void Exit(EnemyStateMachine stateMachine)
    {
        // 交给 PatrolState.Enter 关闭瞄准/射击，这里无需额外清理
    }

    /// <summary>围绕玩家左右移动：每隔 1.5~3 秒随机换向，沿自身 right 轴平移。</summary>
    private void UpdateStrafe(EnemyStateMachine stateMachine)
    {
        if (stateMachine.Movement == null) return;

        strafeTimer -= Time.deltaTime;
        if (strafeTimer <= 0f)
        {
            // 随机选择左移或右移
            strafeDirection = Random.value < 0.5f ? -1f : 1f;
            strafeTimer = Random.Range(1.5f, 3f);
        }

        stateMachine.Movement.Strafe(strafeDirection, stateMachine.Movement.StrafeSpeed);
    }
}
