using UnityEngine;

/// <summary>
/// 警戒状态（确认 / 调查 / 搜索）。
///
/// 按进入来源区分两条主线：
///
///   1. 第一次发现（来自 Patrol，尚未确认目标）：
///      PlayerVisible：先看向玩家位置（转向对准），再持续可见 reactionTime 秒，确认是否为真实目标
///        成功（计时结束仍可见）→ ConfirmPlayer → Combat；
///        中途玩家消失 → 直接返回 Patrol（未确认目标：不保留、不寻找、不进 MoveToLastPosition/Search）
///
///   2. 已确认目标后寻找（来自 Combat，玩家丢失）：不再重新确认，直接寻找已知目标
///        → 仍可见 → ConfirmPlayer → Combat
///        → 消失 → MoveToLastPosition → Search（原地旋转搜索：顺时针 360° → 逆时针 360°）
///
///   寻找流程（MoveToLastPosition / Search）只允许由 ConfirmPlayer（玩家消失）或 Combat（丢失玩家）进入，
///   途中重新发现玩家 → 直接 ConfirmPlayer → Combat，不经过 PlayerVisible、不重新等待 reactionTime。
///
///   搜索升级（模拟推理玩家位置）：
///     第一次搜索结束仍无发现 → 不直接返回巡逻。敌人用玩家最后已知信息（最后位置 / 最后移动方向 /
///     推测移动速度 / 最后出现时间）模拟推理一个 predictedPlayerPosition，前往该推理位置并再次原地搜索
///     （顺时针 360° → 逆时针 360°）。第二次搜索仍无发现 → 放弃寻找，直接返回 Patrol。
///     不增加等待时间 / 放弃阶段 / 无限搜索。推理计算在 EnemyStateMachine.CalculatePredictedPlayerPosition，
///     不读取玩家实时位置。
///
/// lastKnownPlayerPosition 的持续更新由 EnemyStateMachine 统一处理（任何状态只要可见就更新）；
/// 本状态移动阶段只朝"进入调查时快照的固定位置"走，不实时读取更新值——
/// 更新位置 ≠ 移动追踪，Alert 不追玩家。
/// </summary>
public class EnemyAlertState : IEnemyState
{
    /// <summary>警戒内部阶段。</summary>
    private enum Phase
    {
        PlayerVisible,           // 第一次发现确认：看向玩家位置并持续可见 reactionTime 秒（玩家消失 → 直接返回巡逻）
        ConfirmPlayer,           // 确认目标：玩家可见 → 立即进入 Combat；消失 → 寻找玩家最后位置
        MoveToLastPosition,      // 移动到玩家最后出现的位置（寻找已知目标）
        Search,                  // 原地旋转搜索（顺时针 360° → 逆时针 360°）
        MoveToPredictedPosition  // 移动到模拟推理出的玩家位置（第一次搜索失败后的二次调查）
    }

    private Phase phase;
    private bool hasFacedPlayer; // PlayerVisible 是否已完成"看向玩家位置"（转向对准过玩家一次）
    private int searchSegment;   // 搜索旋转段：0 = 第一段（顺时针），1 = 第二段（逆时针）
    private float searchAngle;   // 当前旋转段已旋转角度（度）
    private float confirmTimer;  // 玩家持续可见计时（秒）
    private Vector3 investigatePosition; // 调查目标位置：进入移动阶段时一次性快照，移动阶段不再实时读取
    private bool hasPredicted;           // 第一次搜索失败并进入"模拟推理 + 二次调查"后为 true；第二次搜索失败 → 直接返回巡逻

    /// <summary>当前警戒子状态（PlayerVisible / ConfirmPlayer / MoveToLastPosition / Search），供状态机 Debug 显示。</summary>
    public string CurrentPhase => phase.ToString();

    public void Enter(EnemyStateMachine stateMachine)
    {
        // 切换为警戒视野（270° 大范围搜索；FOV 由状态机统一管理，检测与 Debug 都读 CurrentFov）
        stateMachine.ApplyFov(stateMachine.AlertFov);

        // 警戒中不武装，关闭瞄准与射击
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

        searchSegment = 0; // 从第一段（顺时针）开始搜索
        searchAngle = 0f;
        confirmTimer = 0f;
        hasPredicted = false; // 尚未做过模拟推理（进入搜索流程时总是从第一次搜索开始）

        // 区分进入来源：
        //   第一次发现（来自 Patrol）→ PlayerVisible：看向玩家位置并持续可见计时，确认是否为真实目标
        //   已确认目标后寻找（来自 Combat）→ 不再重新确认：仍可见 → ConfirmPlayer；消失 → MoveToLastPosition 寻找
        bool firstDiscovery = stateMachine.PreviousState is EnemyPatrolState;
        if (firstDiscovery)
        {
            // lastKnownPlayerPosition 已由状态机 UpdateKnownPlayerPosition 在每帧开头更新（可见时 = 玩家当前位置）
            StartPlayerVisible(stateMachine);
        }
        else if (stateMachine.TryDetectPlayer())
        {
            // 玩家仍可见：已确认过目标 → 直接确认，不重新等待反应时间
            phase = Phase.ConfirmPlayer;
        }
        else
        {
            // 玩家丢失 → 移动到玩家最后出现的位置寻找
            StartMoveToLastPosition(stateMachine);
        }
    }

    public void Update(EnemyStateMachine stateMachine)
    {
        switch (phase)
        {
            case Phase.PlayerVisible:          UpdatePlayerVisible(stateMachine);          break;
            case Phase.ConfirmPlayer:          UpdateConfirmPlayer(stateMachine);          break;
            case Phase.MoveToLastPosition:     UpdateMoveToInvestigatePosition(stateMachine); break;
            case Phase.MoveToPredictedPosition: UpdateMoveToInvestigatePosition(stateMachine); break;
            case Phase.Search:                 UpdateSearch(stateMachine);                 break;
        }
    }

    public void Exit(EnemyStateMachine stateMachine)
    {
        // 交给下一个状态的 Enter 处理
    }

    /// <summary>
    /// 阶段一（PlayerVisible，仅第一次发现）：确认第一次发现的疑似目标是否为真实目标。
    /// 先看向玩家位置（持续转向对准，对准后才开始计时），再持续可见 reactionTime 秒；
    /// 计时结束仍可见 → ConfirmPlayer；中途玩家消失 → 直接返回 Patrol（未确认目标：不保留、不寻找）。
    /// </summary>
    private void UpdatePlayerVisible(EnemyStateMachine stateMachine)
    {
        // 玩家消失（转向对准期间或计时期间）→ 未确认目标，直接返回巡逻
        if (!stateMachine.TryDetectPlayer())
        {
            stateMachine.SwitchState(stateMachine.PatrolState);
            return;
        }

        // 先看向玩家位置：未对准过玩家 → 持续转向对准（无移动组件时跳过转向）
        if (!hasFacedPlayer)
        {
            if (stateMachine.Movement != null)
                stateMachine.Movement.RotateTowards(stateMachine.Player.position);

            if (stateMachine.Movement == null || stateMachine.IsFacingPlayer())
                hasFacedPlayer = true;
            return; // 未完成看向玩家位置前不计时
        }

        // 已对准玩家且持续可见 → 持续计时
        confirmTimer -= Time.deltaTime;
        if (confirmTimer <= 0f)
            phase = Phase.ConfirmPlayer;
    }

    /// <summary>
    /// 阶段二（确认目标）：表示敌人已确认目标，只负责"玩家是否仍存在"。
    /// 玩家可见 → 立即进入 Combat（无额外等待）；玩家消失 → 进入寻找玩家最后位置流程。
    /// 不负责等待反应时间 / 搜索（PlayerVisible / Search 负责那些）。
    /// </summary>
    private void UpdateConfirmPlayer(EnemyStateMachine stateMachine)
    {
        if (stateMachine.TryDetectPlayer())
            stateMachine.SwitchState(stateMachine.CombatState);
        else
            StartMoveToLastPosition(stateMachine);
    }

    /// <summary>开始 PlayerVisible：需要先"看向玩家位置"，再持续可见 reactionTime 秒后确认目标（仅第一次发现时调用）。</summary>
    private void StartPlayerVisible(EnemyStateMachine stateMachine)
    {
        hasFacedPlayer = false; // 尚未看向玩家位置
        confirmTimer = stateMachine.ReactionTime;
        phase = Phase.PlayerVisible;
    }

    /// <summary>
    /// 进入 ConfirmPlayer：表示敌人已确认目标，只检查玩家是否仍可见（可见 → Combat；消失 → 寻找）。
    /// 用于寻找流程（MoveToLastPosition / Search）重新发现玩家时，不重新等待 reactionTime、不经过 PlayerVisible。
    /// </summary>
    private void StartConfirmPlayer(EnemyStateMachine stateMachine)
    {
        // 从移动/搜索中打断 → 停止移动与旋转，避免残留寻路/旋转
        if (stateMachine.Movement != null)
        {
            stateMachine.Movement.StopMove();
            stateMachine.Movement.StopRotation();
        }

        phase = Phase.ConfirmPlayer;
    }

    /// <summary>
    /// 开始调查（寻找已知目标）：一次性快照玩家最后已知位置作为固定调查目标。
    /// 之后移动阶段只朝这个固定点走，不再实时读取 LastKnownPlayerPosition，
    /// 避免把"可见即持续更新"的位置数据当作移动目标（更新位置 ≠ 移动追踪，Alert 不追玩家）。
    /// </summary>
    private void StartMoveToLastPosition(EnemyStateMachine stateMachine)
    {
        investigatePosition = stateMachine.LastKnownPlayerPosition;
        // 移动阶段需要朝移动方向转向（恢复移动转向控制）
        if (stateMachine.Movement != null)
            stateMachine.Movement.FaceMovementDirection = true;
        phase = Phase.MoveToLastPosition;
    }

    /// <summary>
    /// 阶段三（寻找）：移动到进入调查时固定的调查目标位置，到达后开始搜索。途中重新发现玩家 → 直接确认。
    /// MoveToLastPosition（最后已知位置）与 MoveToPredictedPosition（模拟推理位置）共用，差异仅在调查目标位置。
    /// </summary>
    private void UpdateMoveToInvestigatePosition(EnemyStateMachine stateMachine)
    {
        // 移动途中重新发现玩家 → 直接进入 ConfirmPlayer（已确认过目标，不重新等待 reactionTime、不经过 PlayerVisible）
        if (stateMachine.TryDetectPlayer())
        {
            StartConfirmPlayer(stateMachine);
            return;
        }

        if (stateMachine.Movement == null)
        {
            StartSearch();
            return;
        }

        bool arrived = stateMachine.Movement.MoveTowards(
            investigatePosition, stateMachine.Movement.MoveSpeed);
        if (arrived)
        {
            stateMachine.Movement.StopMove();
            StartSearch();
        }
    }

    /// <summary>阶段四（搜索）：原地旋转搜索玩家。第一段顺时针 360° → 第二段逆时针 360°，连续不跳角。</summary>
    private void UpdateSearch(EnemyStateMachine stateMachine)
    {
        // 搜索中发现玩家 → 直接进入 ConfirmPlayer（不经过 PlayerVisible，不重新等待 reactionTime）
        if (stateMachine.TryDetectPlayer())
        {
            StartConfirmPlayer(stateMachine);
            return;
        }

        // 原地持续旋转：RotateAroundY 参数为每秒角度，内部会乘 deltaTime，直接传速度即可
        // 旋转方向：第一段顺时针（负 = 向右/顺时针），第二段逆时针（正 = 向左/逆时针）
        float rotateSpeed = stateMachine.AlertSearchRotateSpeed;
        float direction = searchSegment == 0 ? -1f : 1f;
        if (stateMachine.Movement != null)
            stateMachine.Movement.RotateAroundY(direction * rotateSpeed);

        // 累计当前段已旋转角度（只算转动量，与方向无关）
        searchAngle += rotateSpeed * Time.deltaTime;

        // 当前段转满 AlertSearchAngle 度（默认 360）：
        //   第一段完成 → 无缝切换第二段（反向旋转，不跳回初始角度）
        //   第二段也完成（本轮搜索结束）：
        //     第一次搜索失败 → 模拟推理玩家位置，前往推理位置进行第二次调查（不直接返回巡逻）
        //     第二次搜索也失败 → 放弃寻找，直接返回巡逻（无等待、无额外阶段）
        if (searchAngle >= stateMachine.AlertSearchAngle)
        {
            if (searchSegment == 0)
            {
                searchSegment = 1;
                searchAngle = 0f;
            }
            else
            {
                if (hasPredicted)
                    stateMachine.SwitchState(stateMachine.PatrolState);
                else
                    StartMoveToPredictedPosition(stateMachine);
            }
        }
    }

    /// <summary>
    /// 第一次搜索失败 → 二次调查：模拟推理玩家可能移动到的位置并前往。
    /// 推理位置由 EnemyStateMachine.CalculatePredictedPlayerPosition 计算（不读取玩家实时位置，
    /// 只用最后已知位置 / 最后移动方向 / 推测速度 / 最后出现时间）。到达后复用同一搜索逻辑再次搜索。
    /// </summary>
    private void StartMoveToPredictedPosition(EnemyStateMachine stateMachine)
    {
        hasPredicted = true; // 已进入二次调查：第二次搜索失败将直接返回巡逻
        investigatePosition = stateMachine.CalculatePredictedPlayerPosition();
        // 移动阶段需要朝移动方向转向（恢复移动转向控制）
        if (stateMachine.Movement != null)
            stateMachine.Movement.FaceMovementDirection = true;
        phase = Phase.MoveToPredictedPosition;
    }

    private void StartSearch()
    {
        phase = Phase.Search;
        searchSegment = 0; // 从第一段（顺时针）开始搜索
        searchAngle = 0f;
    }
}
