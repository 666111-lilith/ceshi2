using UnityEngine;

namespace Palworks.Character
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PuppetPlayerController : MonoBehaviour
    {
        public float moveSpeed = 4.8f;
        [Min(1)] public float sprintMultiplier = 1.45f;
        [Min(1)] public float dashMultiplier = 2.8f;
        [Min(.05f)] public float dashDuration = .24f;
        [Min(.1f)] public float dashCooldown = .6f;
        [Min(.1f)] public float jumpHeight = 1.2f;
        [Min(1)] public float gravity = 24f;
        [Min(0)] public float jumpPreparation = .08f;
        public Transform visuals;
        public PuppetAnimator puppet;
        CharacterController motor;
        float verticalSpeed, dashRemaining, dashRecovery, jumpBuffer, coyote;
        float preparationRemaining, airTime;
        bool jumpMoving, airborne;
        Vector2 dashDirection, facing = Vector2.right;

        public bool IsDashing => dashRemaining > 0;
        public bool IsAirborne => airborne;
        public bool IsSprinting { get; private set; }
        public float VerticalSpeed => verticalSpeed;

        void Awake() { motor = GetComponent<CharacterController>(); }
        void OnDisable()
        {
            verticalSpeed=0; dashRemaining=0; dashRecovery=0;
            preparationRemaining=0; jumpBuffer=0; coyote=0; airborne=false;
        }
        void Update()
        {
            StepMovement(new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")),
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl),
                Input.GetKeyDown(KeyCode.Space),Time.deltaTime);
        }

        public void StepMovement(Vector2 input,bool sprint,bool dashPressed,bool jumpPressed,float deltaTime)
        {
            if(motor==null) motor=GetComponent<CharacterController>();
            if(motor==null || !motor.enabled || deltaTime<=0) return;
            deltaTime=Mathf.Min(deltaTime,.05f);
            input=Vector2.ClampMagnitude(input,1);
            bool moving=input.sqrMagnitude>.001f;
            if(moving) facing=input.normalized;
            bool grounded=motor.isGrounded;
            dashRecovery=Mathf.Max(0,dashRecovery-deltaTime);
            if(grounded) coyote=.1f; else coyote=Mathf.Max(0,coyote-deltaTime);
            jumpBuffer=jumpPressed ? .12f : Mathf.Max(0,jumpBuffer-deltaTime);
            if(grounded && verticalSpeed<0) verticalSpeed=-2;
            IsSprinting=sprint && moving && !IsDashing;
            if(dashPressed && dashRecovery<=0 && preparationRemaining<=0)
            {
                dashDirection=moving?input.normalized:facing;
                if(dashDirection.sqrMagnitude<.01f)
                    dashDirection=Vector2.right*(visuals!=null && visuals.localScale.x<0?-1:1);
                dashRemaining=dashDuration; dashRecovery=dashDuration+dashCooldown;
                puppet?.PlayAction("Dash",dashDuration);
            }
            if(jumpBuffer>0 && coyote>0 && preparationRemaining<=0 && !IsDashing && !airborne)
            {
                jumpBuffer=0; coyote=0; jumpMoving=moving;
                preparationRemaining=Mathf.Max(.001f,jumpPreparation);
                puppet?.PlayAction(jumpMoving?"JumpPrepareMove":"JumpPrepareIdle",preparationRemaining);
            }
            if(preparationRemaining>0)
            {
                preparationRemaining-=deltaTime;
                if(preparationRemaining<=0)
                {
                    verticalSpeed=Mathf.Sqrt(2*gravity*jumpHeight);
                    airborne=true; airTime=0;
                }
            }
            Vector2 velocity=input*moveSpeed*(IsSprinting?sprintMultiplier:1);
            bool dashFrame=dashRemaining>0;
            if(dashFrame)
            {
                velocity=dashDirection*moveSpeed*dashMultiplier;
                dashRemaining=Mathf.Max(0,dashRemaining-deltaTime);
            }
            else if(preparationRemaining>0) velocity*=.35f;
            verticalSpeed-=gravity*deltaTime;
            CollisionFlags flags=motor.Move(new Vector3(velocity.x,verticalSpeed,velocity.y)*deltaTime);
            if((flags&CollisionFlags.Above)!=0 && verticalSpeed>0) verticalSpeed=0;
            bool landed=(flags&CollisionFlags.Below)!=0 && verticalSpeed<=0;
            if(landed)
            {
                verticalSpeed=-2;
                if(airborne)
                {
                    airborne=false;
                    puppet?.PlayAction(moving?"LandMove":"LandIdle",moving ? .14f : .18f);
                }
            }
            else if(!grounded && preparationRemaining<=0) airborne=true;
            Vector2 direction=dashFrame?dashDirection:input;
            if(Mathf.Abs(direction.x)>.01f && visuals!=null)
            {
                Vector3 scale=visuals.localScale;
                scale.x=Mathf.Abs(scale.x)*(direction.x<0?-1:1);
                visuals.localScale=scale;
            }
            if(puppet!=null)
            {
                if(airborne && !dashFrame)
                {
                    airTime+=deltaTime;
                    float flight=2*Mathf.Sqrt(2*gravity*jumpHeight)/gravity;
                    puppet.SampleAction(jumpMoving?"JumpMove":"JumpIdle",airTime/Mathf.Max(.1f,flight));
                }
                else puppet.SetMotion(moving,IsSprinting);
            }
        }
    }
}
