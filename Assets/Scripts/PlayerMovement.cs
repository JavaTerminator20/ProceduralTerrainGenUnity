using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour {
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;              // [SerializeField] je tukaj zato da je var viden v inspectorju in hkrati privaten
    [SerializeField] private float sprintMultiplier = 1.8f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -20f;

    [Header("Look")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float lookSensitivity = 0.15f;
    [SerializeField] private float maxPitchAngle = 80f;

    [Header("Ground Detection")]
    [SerializeField] private float groundCheckDistance = 0.1f; // extra raycast for slopes
    [SerializeField] private LayerMask groundMask;

    // ── Component refs ──────────────────────────────────────────
    private CharacterController cc;

    // ── Input state ─────────────────────────────────────────────
    // Stored here because input callbacks fire on change,
    // but we apply movement every Update
    private Vector2 moveInput;
    private float upDownInput;
    private Vector2 lookInput;
    private bool isSprinting;
    private bool jumpRequested;

    // ── Internal state ───────────────────────────────────────────
    private Vector3 velocity;      // only y is used (vertical/gravity)
    private float cameraPitch;     // current vertical camera angle
    private float cameraYaw;       // current horizontal camera angle

    // ────────────────────────────────────────────────────────────

    public bool useGravity = true; // whether to apply gravity and jumping (for testing purposes)
    private void Awake() {
        cc = GetComponent<CharacterController>();

        // Lock and hide cursor for FPS-style look
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = true;

        groundMask = LayerMask.GetMask("Default");
    }

    private void Update() {
        HandleCameraPosition();
        HandleLook();
        HandleMovement();
        if (useGravity) HandleGravityAndJump();  // only apply gravity and jumping if enabled
    }

    // ── Look ─────────────────────────────────────────────────────
    private void HandleLook() {
        transform.Rotate(Vector3.up * lookInput.x * lookSensitivity);   // horizontal mouse movement rotates the player left/right

        // Rotate camera vertically (clamped so you can't flip)
        cameraPitch -= lookInput.y * lookSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch, -maxPitchAngle, maxPitchAngle);

        cameraYaw -= lookInput.x * lookSensitivity * -1;
        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
    }

    private void HandleMovement() {
        // moveInput is in local space: x = strafe, y = forward
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        // Add vertical movement (flying)
        if (!useGravity) {
            move += Vector3.up * upDownInput;
        }

        float currentSpeed = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        cc.Move(move * currentSpeed * Time.deltaTime);
    }

    private void HandleGravityAndJump() {
        Vector3 feetPosition = transform.position + Vector3.down * (transform.localScale.y / 2f);
        bool grounded = Physics.Raycast(feetPosition, Vector3.down, groundCheckDistance, groundMask);

        // Small negative keeps cc.isGrounded reliable on slopes
        if (grounded && velocity.y < 0f)
            velocity.y = -2f;

        // Jump — only consume the request if grounded  
        if (jumpRequested && grounded) {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);    // Physics formula: v = sqrt(h * -2 * g)
        }

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        cc.Move(velocity * Time.deltaTime);
    }

    private void HandleCameraPosition() {
        // Keep camera at character's head height
        Vector3 cameraPos = transform.position;
        cameraPos.y += cc.height * 1f; // adjust as needed for head position
        cameraTransform.position = cameraPos;
    }


    // ── Input callbacks (called by PlayerInput component) ────────

    public void OnMove(InputValue value) {
        moveInput = value.Get<Vector2>();   // (0,0) when no keys held, (1,0) = right, (0,1) = forward etc.
    }

    public void OnLook(InputValue value) {
        lookInput = value.Get<Vector2>();   // Mouse delta: how much mouse moved this frame
    }

    public void OnJump(InputValue value) {      // interaction is set to "Press and Release" - so it get's called both times
        jumpRequested = value.isPressed;        // so that we can hold space and jump as soon as we hit the ground, instead of having to time it perfectly.
                                                // We use a flag instead of jumping directly here because the callback timing isn't always in sync with Update
    }

    public void OnSprint(InputValue value) {    // must be set to Value and not Button in Input Actions
        isSprinting = value.isPressed;  // true while sprint button held
    }

    public void OnFly(InputValue value) {
        if (value.isPressed) {
            useGravity = !useGravity;
        }
    }

    public void OnUp(InputValue value) {
        if (useGravity) return; // only allow flying if gravity is disabled

        upDownInput = value.isPressed ? 1f : 0f; // 1 if pressed, 0 if released
    }

    public void OnDown(InputValue value) {
        if (useGravity) return; // only allow flying if gravity is disabled

        upDownInput = value.isPressed ? -1f : 0f; // -1 if pressed, 0 if released
    }

}