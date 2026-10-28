using UnityEngine;

public class PlayerAnimator : MonoBehaviour
{
    private Animator animator;

    [Header("Animation Parameters")]
    [SerializeField] private string walkUpParam = "isWalkUp";
    [SerializeField] private string walkDownParam = "isWalkDown";
    [SerializeField] private string walkLeftParam = "isWalkLeft";
    [SerializeField] private string walkRightParam = "isWalkRight";

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Animator 컴포넌트가 없습니다.");
        }
    }

    void Update()
    {
        if (GameStateManager.IsUIMode)
        {
            UpdateAnimation(Vector3.zero);
            return;
        }
        UpdateAnimation(GetComponentInParent<Player>().HandleMovement());
    }

    public void UpdateAnimation(Vector3 movement)
    {
        if (movement.magnitude == 0)
        {
            SetAllWalkParams(false);
            return;
        }

        SetAllWalkParams(false); 
        
        Vector3 normalizedMovement = movement.normalized;

        if (Mathf.Abs(normalizedMovement.y) >= Mathf.Abs(normalizedMovement.x))
        {
            if (normalizedMovement.y > 0)
            {
                animator.SetBool(walkUpParam, true);
            }
            else
            {
                animator.SetBool(walkDownParam, true);
            }
        }
        else
        {
            if (normalizedMovement.x > 0)
            {
                animator.SetBool(walkRightParam, true);
            }
            else
            {
                animator.SetBool(walkLeftParam, true);
            }
        }
        animator.SetFloat("last.x", movement.x);
        animator.SetFloat("last.y", movement.y);
    }
    
    private void SetAllWalkParams(bool state)
    {
        animator.SetBool(walkUpParam, state);
        animator.SetBool(walkDownParam, state);
        animator.SetBool(walkLeftParam, state);
        animator.SetBool(walkRightParam, state);
    }
}