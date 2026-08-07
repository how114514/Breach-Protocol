using UnityEngine;

/// <summary>
/// 巡逻状态：非警戒，沿固定巡逻点循环移动。
/// 进入时关闭瞄准与射击（未警戒不武装），避免巡逻中走火；
/// 到达巡逻点后原地左右旋转观察并持续检测玩家（等待旋转由 PatrolController 处理）。
/// 发现玩家 → 进入 Alert（不直接进入 Combat）。玩家位置更新由 EnemyStateMachine 统一处理。
/// 具体移动由 EnemyPatrolController + EnemyMovement 完成，本状态不直接操作位移。
/// </summary>
public class EnemyPatrolState : IEnemyState
{
    public void Enter(EnemyStateMachine stateMachine)
    {
        // 切换为巡逻视野（FOV 由状态机统一管理，检测与 Debug 都读 CurrentFov）
        stateMachine.ApplyFov(stateMachine.PatrolFov);

        // 非警戒：关闭瞄准与射击
        if (stateMachine.AimController != null)
            stateMachine.AimController.enabled = false;
        if (stateMachine.ShootController != null)
            stateMachine.ShootController.enabled = false;

        // 移动由 NavMeshAgent 驱动，朝向移动方向；清掉上一状态残留的寻路
        if (stateMachine.Movement != null)
        {
            stateMachine.Movement.FaceMovementDirection = true;
            stateMachine.Movement.StopMove();
        }

        // 开始 / 恢复巡逻（战斗结束后从上次巡逻点继续）
        if (stateMachine.PatrolController != null)
            stateMachine.PatrolController.StartPatrol();
    }

    public void Update(EnemyStateMachine stateMachine)
    {
        // 沿巡逻路线移动：移动 / 到达等待（原地观察旋转）/ 切换巡逻点，全部由 PatrolController 处理
        if (stateMachine.PatrolController != null)
            stateMachine.PatrolController.UpdatePatrol(Time.deltaTime);

        // 第一次发现玩家 → 进入 Alert 调查，不直接进入 Combat（玩家位置已由状态机统一更新）
        if (stateMachine.TryDetectPlayer())
            stateMachine.SwitchState(stateMachine.AlertState);
    }

    public void Exit(EnemyStateMachine stateMachine)
    {
        // 不需要清理：AlertState.Enter 会处理瞄准/射击与移动
    }
}
