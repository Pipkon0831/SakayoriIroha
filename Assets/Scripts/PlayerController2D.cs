using UnityEngine;

public class PlayerController2D : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f; // 移动速度
    [SerializeField] private float moveSmooth = 0.1f; // 移动平滑系数（越小越灵敏）

    [SerializeField] private GameController gameController;

    private Vector2 inputDirection; // 输入方向
    private Vector2 smoothInputVelocity; // 平滑输入缓存
    private Vector2 currentVelocity; // 当前移动速度

    private void Awake()
    {
        if (gameController == null)
        {
            gameController = FindObjectOfType<GameController>();
        }
    }

    private void Update()
    {
        GetPlayerInput();

        CalculateSmoothMovement();

        if (gameController.IsPlayerLockedInRoom())
        {
            currentVelocity = ValidateMovement(currentVelocity);
        }

        ApplyMovement();
    }

    private void GetPlayerInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        inputDirection = new Vector2(horizontal, vertical).normalized;
    }

    private void CalculateSmoothMovement()
    {
        inputDirection = Vector2.SmoothDamp(
            inputDirection,
            Vector2.zero,
            ref smoothInputVelocity,
            moveSmooth
        );

        currentVelocity = inputDirection * moveSpeed;
    }

    private Vector2 ValidateMovement(Vector2 desiredVelocity)
    {
        RoomData currentRoom = gameController.GetCurrentPlayerRoom();
        if (currentRoom == null || currentRoom.floorPositions == null) return desiredVelocity;

        Vector3 targetPos = transform.position + new Vector3(desiredVelocity.x, desiredVelocity.y, 0) * Time.deltaTime;
        
        Vector2Int targetGridPos = new Vector2Int(
            Mathf.FloorToInt(targetPos.x),
            Mathf.FloorToInt(targetPos.y)
        );

        bool isTargetValid = currentRoom.floorPositions.Contains(targetGridPos);

        if (isTargetValid)
        {
            return desiredVelocity;
        }
        else
        {
            Vector2 validVelocity = Vector2.zero;

            Vector3 targetPosX = transform.position + new Vector3(desiredVelocity.x, 0, 0) * Time.deltaTime;
            Vector2Int targetGridX = new Vector2Int(Mathf.FloorToInt(targetPosX.x), Mathf.FloorToInt(transform.position.y));
            if (currentRoom.floorPositions.Contains(targetGridX))
            {
                validVelocity.x = desiredVelocity.x;
            }

            Vector3 targetPosY = transform.position + new Vector3(0, desiredVelocity.y, 0) * Time.deltaTime;
            Vector2Int targetGridY = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(targetPosY.y));
            if (currentRoom.floorPositions.Contains(targetGridY))
            {
                validVelocity.y = desiredVelocity.y;
            }

            return validVelocity;
        }
    }

    private void ApplyMovement()
    {
        transform.Translate(currentVelocity * Time.deltaTime);
    }

    private void OnDrawGizmos()
    {
        if (gameController != null && gameController.IsPlayerLockedInRoom())
        {
            RoomData currentRoom = gameController.GetCurrentPlayerRoom();
            if (currentRoom != null && currentRoom.floorPositions != null)
            {
                Gizmos.color = Color.red;
                foreach (var pos in currentRoom.floorPositions)
                {
                    Gizmos.DrawWireCube(new Vector3(pos.x + 0.5f, pos.y + 0.5f, 0), Vector3.one * 0.9f);
                }
            }
        }
    }
}