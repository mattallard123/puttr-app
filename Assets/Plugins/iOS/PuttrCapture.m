// The camera for a recorded round, through the native Agora RTC SDK (the one react-native-agora
// wraps in the React Native app). It does what that app's VideoCapture does: join a channel as a
// broadcaster with the camera and microphone on, so the API's cloud recorder can record it. The
// recording itself is started and stopped by the API, not here.
//
// What happens is told to Unity with UnitySendMessage(target, "OnCapture", ...):
//   joined | left | published | video|<state>|<reason> | size|<w>|<h> | error|<code>
//   pose|<json> | pose|none     (a 3D session's framing check: see snapshotAndDetect)
//
// A 3D session (lab) films differently: 1080x1920 at 30 fps, no sound, the front lens first and
// switchable, and a still of the preview about once a second whose body pose Apple's Vision finds
// on the phone. (puttr-app runs MediaPipe for this; Vision finds the same joints the framing check
// uses, head to ankles, with nothing to install. The 3D replay still comes from the server's MediaPipe.)
#import <UIKit/UIKit.h>
#import <Vision/Vision.h>
@import AgoraRtcKit;

extern void UnitySendMessage(const char *object, const char *method, const char *message);
extern UIView *UnityGetGLView(void);

@interface PuttrCapture : NSObject <AgoraRtcEngineDelegate>
@property (nonatomic, strong) AgoraRtcEngineKit *engine;
@property (nonatomic, strong) UIView *preview;
@property (nonatomic, strong) UIView *remote; // the other Player in a live 1v1 (GameLobby's full-screen RtcSurfaceView)
@property (nonatomic, copy) NSString *target;
@end

@implementation PuttrCapture

+ (instancetype)shared
{
    static PuttrCapture *one;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ one = [PuttrCapture new]; });
    return one;
}

- (void)send:(NSString *)message
{
    if (self.target.length == 0) return;
    NSString *to = self.target;
    dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(to.UTF8String, "OnCapture", message.UTF8String); });
}

- (void)startWithAppId:(NSString *)appId token:(NSString *)token channel:(NSString *)channel uid:(NSUInteger)uid lab:(BOOL)lab
{
    if (self.engine == nil) self.engine = [AgoraRtcEngineKit sharedEngineWithAppId:appId delegate:self];
    self.engine.delegate = self;
    [self.engine enableVideo];
    if (lab)
    {
        // As LabVideoCapture: portrait 1080x1920, 30 fps, 4 Mbps (at least 1.5), frame rate kept over quality; no sound.
        AgoraVideoEncoderConfiguration *config = [[AgoraVideoEncoderConfiguration alloc] initWithSize:CGSizeMake(1080, 1920) frameRate:AgoraVideoFrameRateFps30 bitrate:4000 orientationMode:AgoraVideoOutputOrientationModeFixedPortrait mirrorMode:AgoraVideoMirrorModeAuto];
        config.minBitrate = 1500;
        config.degradationPreference = AgoraDegradationMaintainFramerate;
        [self.engine setVideoEncoderConfiguration:config];
        [self.engine disableAudio];
    }
    else [self.engine enableAudio];
    [self.engine setChannelProfile:AgoraChannelProfileCommunication];
    [self.engine leaveChannel:nil]; // a round left a moment ago may not have let go yet

    if (self.preview == nil)
    {
        self.preview = [[UIView alloc] initWithFrame:CGRectMake(0, 0, 90, 160)];
        self.preview.userInteractionEnabled = NO;
        self.preview.layer.cornerRadius = 12;
        self.preview.layer.masksToBounds = YES;
        self.preview.layer.borderWidth = 1;
        self.preview.layer.borderColor = [UIColor colorWithWhite:1 alpha:0.25].CGColor;
        self.preview.hidden = YES;
    }
    UIView *host = UnityGetGLView();
    if (self.preview.superview != host) [host addSubview:self.preview];

    AgoraRtcVideoCanvas *canvas = [AgoraRtcVideoCanvas new];
    canvas.uid = 0;
    canvas.view = self.preview;
    canvas.renderMode = lab ? AgoraVideoRenderModeFit : AgoraVideoRenderModeHidden;
    [self.engine setupLocalVideo:canvas];
    [self.engine startPreview];

    AgoraRtcChannelMediaOptions *options = [AgoraRtcChannelMediaOptions new];
    options.clientRoleType = AgoraClientRoleBroadcaster;
    options.publishCameraTrack = YES;
    options.publishMicrophoneTrack = !lab;
    int result = [self.engine joinChannelByToken:token channelId:channel uid:uid mediaOptions:options joinSuccess:nil];
    if (result != 0) [self send:[NSString stringWithFormat:@"error|%d", result]];
}

/// Where the Player sees themselves: a share of the screen, measured from the top left. Zero width hides it.
- (void)placeX:(float)x y:(float)y width:(float)w height:(float)h
{
    if (self.preview == nil) return;
    CGSize size = self.preview.superview.bounds.size;
    self.preview.hidden = w <= 0 || h <= 0;
    self.preview.frame = CGRectMake(x * size.width, y * size.height, w * size.width, h * size.height);
}

/// The other Player's video: a share of the screen; zero width hides it. It sits under the Player's own preview.
- (void)placeRemoteX:(float)x y:(float)y width:(float)w height:(float)h
{
    if (self.remote == nil)
    {
        self.remote = [[UIView alloc] initWithFrame:CGRectZero];
        self.remote.userInteractionEnabled = NO;
        self.remote.backgroundColor = UIColor.blackColor;
        self.remote.hidden = YES;
    }
    UIView *host = UnityGetGLView();
    if (self.remote.superview != host) { if (self.preview.superview == host) [host insertSubview:self.remote belowSubview:self.preview]; else [host addSubview:self.remote]; }
    CGSize size = host.bounds.size;
    self.remote.hidden = w <= 0 || h <= 0;
    self.remote.frame = CGRectMake(x * size.width, y * size.height, w * size.width, h * size.height);
}

- (void)mute:(BOOL)muted { [self.engine muteLocalAudioStream:muted]; }

- (void)switchCamera { [self.engine switchCamera]; }

/// The frame around the preview: the framing check's colour (red, amber, green), or none.
- (void)borderRed:(float)r green:(float)g blue:(float)b width:(float)w
{
    self.preview.layer.borderColor = [UIColor colorWithRed:r green:g blue:b alpha:1].CGColor;
    self.preview.layer.borderWidth = w;
}

/// A still of what the camera sees; its body pose follows as pose|... (snapshotTaken below).
- (void)snapshot
{
    NSString *path = [NSTemporaryDirectory() stringByAppendingPathComponent:@"puttr-framing.jpg"];
    [self.engine takeSnapshot:0 filePath:path];
}

- (void)rtcEngine:(AgoraRtcEngineKit *)engine snapshotTaken:(NSUInteger)uid filePath:(NSString *)filePath width:(NSInteger)width height:(NSInteger)height errCode:(NSInteger)errCode
{
    if (errCode != 0) { [self send:@"pose|none"]; return; }
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{ [self detectPoseAt:filePath width:width height:height]; });
}

// Vision's body pose, as the app's detector reports it: joint name -> {x, y, inFrame}, x and y 0 to 1
// from the top left, inFrame the joint's confidence. Names are MediaPipe's, so the framing check is the app's.
- (void)detectPoseAt:(NSString *)path width:(NSInteger)width height:(NSInteger)height
{
    VNDetectHumanBodyPoseRequest *request = [VNDetectHumanBodyPoseRequest new];
    VNImageRequestHandler *handler = [[VNImageRequestHandler alloc] initWithURL:[NSURL fileURLWithPath:path] options:@{}];
    NSError *error = nil;
    if (![handler performRequests:@[request] error:&error] || request.results.count == 0) { [self send:@"pose|none"]; return; }
    VNHumanBodyPoseObservation *body = request.results.firstObject;
    NSDictionary<NSString *, VNHumanBodyPoseObservationJointName> *names = @{
        @"nose": VNHumanBodyPoseObservationJointNameNose,
        @"left_eye": VNHumanBodyPoseObservationJointNameLeftEye, @"right_eye": VNHumanBodyPoseObservationJointNameRightEye,
        @"left_ear": VNHumanBodyPoseObservationJointNameLeftEar, @"right_ear": VNHumanBodyPoseObservationJointNameRightEar,
        @"left_shoulder": VNHumanBodyPoseObservationJointNameLeftShoulder, @"right_shoulder": VNHumanBodyPoseObservationJointNameRightShoulder,
        @"left_elbow": VNHumanBodyPoseObservationJointNameLeftElbow, @"right_elbow": VNHumanBodyPoseObservationJointNameRightElbow,
        @"left_wrist": VNHumanBodyPoseObservationJointNameLeftWrist, @"right_wrist": VNHumanBodyPoseObservationJointNameRightWrist,
        @"left_hip": VNHumanBodyPoseObservationJointNameLeftHip, @"right_hip": VNHumanBodyPoseObservationJointNameRightHip,
        @"left_knee": VNHumanBodyPoseObservationJointNameLeftKnee, @"right_knee": VNHumanBodyPoseObservationJointNameRightKnee,
        @"left_ankle": VNHumanBodyPoseObservationJointNameLeftAnkle, @"right_ankle": VNHumanBodyPoseObservationJointNameRightAnkle,
    };
    NSMutableDictionary *landmarks = [NSMutableDictionary dictionary];
    for (NSString *name in names)
    {
        VNRecognizedPoint *point = [body recognizedPointForJointName:names[name] error:nil];
        if (point == nil) continue;
        landmarks[name] = @{ @"x": @(point.location.x), @"y": @(1.0 - point.location.y), @"inFrame": @(point.confidence) };
    }
    NSData *json = [NSJSONSerialization dataWithJSONObject:@{ @"width": @(width), @"height": @(height), @"landmarks": landmarks } options:0 error:nil];
    [self send:[@"pose|" stringByAppendingString:[[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]]];
}

- (void)stop
{
    [self.engine leaveChannel:nil];
    [self.engine stopPreview];
    [self.preview removeFromSuperview];
    self.preview.hidden = YES;
    [self.remote removeFromSuperview];
    self.remote.hidden = YES;
}

- (void)rtcEngine:(AgoraRtcEngineKit *)engine didJoinedOfUid:(NSUInteger)uid elapsed:(NSInteger)elapsed
{
    if (self.remote != nil)
    {
        AgoraRtcVideoCanvas *canvas = [AgoraRtcVideoCanvas new];
        canvas.uid = uid;
        canvas.view = self.remote;
        canvas.renderMode = AgoraVideoRenderModeHidden;
        [self.engine setupRemoteVideo:canvas];
    }
    [self send:[NSString stringWithFormat:@"remote|%lu", (unsigned long)uid]];
}
- (void)rtcEngine:(AgoraRtcEngineKit *)engine didOfflineOfUid:(NSUInteger)uid reason:(AgoraUserOfflineReason)reason { [self send:@"remote|0"]; }

- (void)rtcEngine:(AgoraRtcEngineKit *)engine didJoinChannel:(NSString *)channel withUid:(NSUInteger)uid elapsed:(NSInteger)elapsed { [self send:@"joined"]; }
- (void)rtcEngine:(AgoraRtcEngineKit *)engine didLeaveChannelWithStats:(AgoraChannelStats *)stats { [self send:@"left"]; }
- (void)rtcEngine:(AgoraRtcEngineKit *)engine didOccurError:(AgoraErrorCode)errorCode { [self send:[NSString stringWithFormat:@"error|%ld", (long)errorCode]]; }
- (void)rtcEngine:(AgoraRtcEngineKit *)engine firstLocalVideoFramePublishedWithElapsed:(NSInteger)elapsed sourceType:(AgoraVideoSourceType)sourceType { [self send:@"published"]; }
- (void)rtcEngine:(AgoraRtcEngineKit *)engine localVideoStateChangedOfState:(AgoraVideoLocalState)state reason:(AgoraLocalVideoStreamReason)reason sourceType:(AgoraVideoSourceType)sourceType
{
    [self send:[NSString stringWithFormat:@"video|%ld|%ld", (long)state, (long)reason]];
}
- (void)rtcEngine:(AgoraRtcEngineKit *)engine videoSizeChangedOfSourceType:(AgoraVideoSourceType)sourceType uid:(NSUInteger)uid size:(CGSize)size rotation:(NSInteger)rotation
{
    if (uid == 0) [self send:[NSString stringWithFormat:@"size|%d|%d", (int)size.width, (int)size.height]];
}

@end

void PuttrCapture_Start(const char *appId, const char *token, const char *channel, int uid, const char *target, int lab)
{
    NSString *app = [NSString stringWithUTF8String:appId ?: ""], *tok = [NSString stringWithUTF8String:token ?: ""];
    NSString *chan = [NSString stringWithUTF8String:channel ?: ""], *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrCapture.shared.target = to;
        [PuttrCapture.shared startWithAppId:app token:tok channel:chan uid:(NSUInteger)uid lab:lab != 0];
    });
}

void PuttrCapture_SwitchCamera(void) { dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared switchCamera]; }); }
void PuttrCapture_Snapshot(void) { dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared snapshot]; }); }
void PuttrCapture_Border(float r, float g, float b, float w) { dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared borderRed:r green:g blue:b width:w]; }); }

void PuttrCapture_Place(float x, float y, float w, float h)
{
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared placeX:x y:y width:w height:h]; });
}

void PuttrCapture_PlaceRemote(float x, float y, float w, float h)
{
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared placeRemoteX:x y:y width:w height:h]; });
}

void PuttrCapture_Mute(int muted) { dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared mute:muted != 0]; }); }

void PuttrCapture_Stop(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared stop]; });
}
