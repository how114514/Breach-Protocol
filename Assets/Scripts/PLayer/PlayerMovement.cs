using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    private InputSystem_Actions inputActions;

    private void Awake()
    {
        inputActions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();
    }

    private void Update()
    {
        // 读取 Move 输入（WASD 复合绑定 -> Vector2）
        Vector2 moveInput = inputActions.Player.Move.ReadValue<Vector2>();

        // 只在 X/Z 平面移动，忽略 Y 轴
        Vector3 direction = new Vector3(moveInput.x, 0f, moveInput.y);
        transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
    }
}
