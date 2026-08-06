using UnityEngine;

/// <summary>
/// 巡逻状态：非警戒，沿固定路线来回巡逻。
/// 进入时关闭瞄准与射击（未警戒不武装），避免巡逻中走火；
/// 每帧沿巡逻路线移动，并检测玩家是否进入警戒范围（距离 + 前方角度 + 视线）。
/// 具体移动由 EnemyPatrolController + EnemyMovement 完成，本状态不直接操作位移。
/// </summary>
public class EnemyPatrolState : IEnemyState
{
    public void Enter(EnemyStateMachine stateMachine)
    {
        // 非警戒：关闭瞄准与射击
        if (stateMachine.AimController != null)
            stateMachine.AimController.enabled = false;
        if (stateMachine.ShootController != null)
            stateMachine.ShootController.enabled = false;

        // 开始 / 恢复巡逻（战斗结束后从上次巡逻点继续）
        if (stateMachine.PatrolController != null)
            stateMachine.PatrolController.StartPatrol();
    }

    public void Update(EnemyStateMachine stateMachine)
    {
        // 沿巡逻路线移动：移动 / 到达等待 / 切换巡逻点，全部由 PatrolController 处理
        if (stateMachine.PatrolController != null)
            stateMachine.PatrolController.UpdatePatrol(Time.deltaTime);

        // 检测到玩家进入警戒范围 → 进入战斗
        if (stateMachine.TryDetectPlayer())
            stateMachine.SwitchState(stateMachine.CombatState);
    }

    public void Exit(EnemyStateMachine stateMachine)
    {
        // 不需要清理：CombatState.Enter 会负责打开瞄准与射击
    }
}
