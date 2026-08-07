using UnityEngine;

/// <summary>
/// 战斗状态：确认玩家为目标，开启瞄准与射击。
/// 瞄准（EnemyAimController）与射击（EnemyShootController）是独立组件，开启后各自按逻辑运作，
/// 本状态只负责"是否处于战斗 / 如何调整站位"的判定，不直接操作枪械旋转与子弹生成。
///
/// 玩家位置记录（统一规则，由 EnemyStateMachine 统一处理）：
///   战斗中只要玩家可见，状态机每帧持续更新 lastKnownPlayerPosition；本状态不重复维护。
///   玩家丢失（离开视野）：状态机停止更新（冻结最后位置），本状态切换到 Alert 搜索。
///
/// 战斗站位（combatMinDistance ~ combatMaxDistance）：
///   - 距离过远：斜向靠近（目的地取玩家方向偏转 combatApproachAngle 处，形成斜线，不直线冲刺）。
///   - 距离合适：停止移动，保持距离，瞄准射击。
///   - 距离过近：后退 / 调整位置。
/// </summary>
public class EnemyCombatState : IEnemyState
{
    private const float CombatApproachAngle = 35f; // 斜向靠近的偏转角（度）
    private const float FlankFlipInterval = 4f;    // 侧翼方向切换间隔（秒）

    private float repositionBias;  // 斜向靠近时的侧翼方向（-1 左 / 1 右）
    private float flankFlipTimer;  // 侧翼方向切换计时
    private string combatMode = "-"; // 当前战斗走位模式（Debug：Approach / Hold / Retreat）

    /// <summary>当前战斗子状态（Approach 靠近 / Hold 保持 / Retreat 后退），供状态机 Debug 显示。</summary>
    public string CurrentSubState => combatMode;

    public void Enter(EnemyStateMachine stateMachine)
    {
        // 切换为战斗视野（保持当前战斗检测逻辑；FOV 由状态机统一管理，检测与 Debug 都读 CurrentFov）
        stateMachine.ApplyFov(stateMachine.CombatFov);

        // 进入战斗：开启瞄准与射击，由各自组件处理旋转与开火时机
        if (stateMachine.AimController != null)
            stateMachine.AimController.enabled = true;
        if (stateMachine.ShootController != null)
            stateMachine.ShootController.enabled = true;

        // 战斗中朝向由瞄准控制器朝玩家控制，不再朝移动方向转向；
        // 显式停止上一阶段（确认/搜索）残留的任何旋转，确保战斗旋转只由瞄准控制器接管
        if (stateMachine.Movement != null)
        {
            stateMachine.Movement.FaceMovementDirection = false;
            stateMachine.Movement.StopRotation();
        }

        repositionBias = Random.value < 0.5f ? -1f : 1f;
        flankFlipTimer = FlankFlipInterval;
    }

    public void Update(EnemyStateMachine stateMachine)
    {
        // 玩家可见（范围内 + 视线无遮挡，不限方向）→ 按距离调整站位（位置更新已由状态机统一处理）
        if (stateMachine.IsPlayerInRangeAndVisible())
        {
            UpdateCombatPositioning(stateMachine);
            return;
        }

        // 玩家丢失：停止更新位置，保留最后一次看到的位置，进入 Alert 搜索
        stateMachine.SwitchState(stateMachine.AlertState);
    }

    public void Exit(EnemyStateMachine stateMachine)
    {
        // 恢复默认移动转向（Alert/Patrol 需要朝移动方向转向）
        if (stateMachine.Movement != null)
        {
            stateMachine.Movement.FaceMovementDirection = true;
            stateMachine.Movement.StopMove();
        }
    }

    /// <summary>根据与玩家的距离调整站位：过远斜向靠近 / 合适保持 / 过近后退。</summary>
    private void UpdateCombatPositioning(EnemyStateMachine stateMachine)
    {
        if (stateMachine.Movement == null) return;

        Vector3 toPlayer = stateMachine.Player.position - stateMachine.transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance < 0.0001f) return; // 与玩家重合，无法判定方向

        Vector3 dirToPlayer = toPlayer / distance;

        if (distance > stateMachine.CombatMaxDistance)
        {
            combatMode = "Approach"; // Debug：过远，斜向靠近
            // 过远：斜向靠近。目的地取"玩家方向偏转 ±combatApproachAngle"处、战斗理想距离（min/max 中点），
            // 敌人沿斜线逼近玩家侧翼，不直线冲刺；到达后即落在 [min, max] 距离带内 → 停止移动。
            Vector3 approachDir = Quaternion.Euler(0f, repositionBias * CombatApproachAngle, 0f) * dirToPlayer;
            Vector3 goal = stateMachine.Player.position
                - approachDir * CombatDesiredDistance(stateMachine);

            // 周期切换侧翼方向，避免总从同一侧靠近 / 卡在障碍物一侧
            flankFlipTimer -= Time.deltaTime;
            if (flankFlipTimer <= 0f)
            {
                repositionBias = -repositionBias;
                flankFlipTimer = FlankFlipInterval;
            }

            stateMachine.Movement.MoveTowards(goal, stateMachine.CombatMoveSpeed);
        }
        else if (distance < stateMachine.CombatMinDistance)
        {
            combatMode = "Retreat"; // Debug：过近，后退
            // 过近：后退到 combatMinDistance 之外（向远离玩家的方向移动）
            Vector3 goal = stateMachine.transform.position
                - dirToPlayer * (stateMachine.CombatMinDistance - distance + 1f);
            stateMachine.Movement.MoveTowards(goal, stateMachine.CombatMoveSpeed);
        }
        else
        {
            combatMode = "Hold"; // Debug：距离合适，保持距离
            // 距离合适：停止接近，保持距离，瞄准射击（瞄准/射击控制器已开启）
            stateMachine.Movement.StopMove();
        }
    }

    /// <summary>战斗理想停留距离（combatMinDistance 与 combatMaxDistance 的中点）。</summary>
    private float CombatDesiredDistance(EnemyStateMachine stateMachine)
        => (stateMachine.CombatMinDistance + stateMachine.CombatMaxDistance) * 0.5f;
}
