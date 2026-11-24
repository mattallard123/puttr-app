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
//
// A 3D session's preview fills the screen above the app's bottom bar, and the guide the Player lines
// up with is drawn here, over the picture (CoachGame.js: the dashed centre line, the live skeleton,
// the red/amber/green frame and the coach's line in a card at the top). Unity draws under this view,
// so anything that must sit on the picture has to be native: PuttrCapture_Guide sets it.
#import <UIKit/UIKit.h>
#import <Vision/Vision.h>
@import AVFoundation;
@import AgoraRtcKit;

extern void UnitySendMessage(const char *object, const char *method, const char *message);
extern UIView *UnityGetGLView(void);

/// The framing guide over a 3D session's picture.
@interface PuttrLabGuide : UIView
@property (nonatomic, copy) NSDictionary *landmarks;   // joint -> {x, y, inFrame}, 0 to 1 of the still
@property (nonatomic) CGFloat aspect;                  // the picture's width / height, 0 until known
@property (nonatomic) BOOL mirror;                     // the front lens: the preview is mirrored, the stills are not
@property (nonatomic, strong) UIColor *status;         // red / amber / green, nil for no frame
@property (nonatomic) CGFloat progress;                // hold-still progress 0 to 1, below the line
@property (nonatomic) BOOL confirmed;
@property (nonatomic, strong) UIView *card;
@property (nonatomic, strong) UILabel *label;
@property (nonatomic, strong) UIView *track, *fill;
@property (nonatomic, strong) UIView *note;            // "Putt 2 of 6 recorded", mid picture, for a moment
@property (nonatomic, strong) UILabel *noteTitle, *noteBody;
@property (nonatomic) NSInteger noteRun;
- (void)showLine:(NSString *)text;
- (void)showNote:(NSString *)title body:(NSString *)body good:(BOOL)good;
@end

@interface PuttrCapture : NSObject <AgoraRtcEngineDelegate>
@property (nonatomic, strong) AgoraRtcEngineKit *engine;
@property (nonatomic, strong) UIView *stage;   // what Place moves: the picture, and in a 3D session the guide over it
@property (nonatomic, strong) UIView *preview; // Agora draws the local camera here
@property (nonatomic, strong) PuttrLabGuide *guide;
@property (nonatomic, strong) UIView *remote; // the other Player in a live 1v1 (GameLobby's full-screen RtcSurfaceView)
@property (nonatomic, copy) NSString *target;
@property (nonatomic) CGRect share;            // the last Place, kept so a Place before the camera starts still counts
@property (nonatomic) BOOL lab, front, silent;
@end

static UIColor *GuideGreen(CGFloat a) { return [UIColor colorWithRed:0x2D / 255.0 green:0x8C / 255.0 blue:0x3C / 255.0 alpha:a]; }
static UIColor *GuideRed(CGFloat a) { return [UIColor colorWithRed:1 green:0x52 / 255.0 blue:0x52 / 255.0 alpha:a]; }

/// The app's own sound back on the speaker: the playback category, the session active, and Unity's sound engine told
/// to start again (it restarts on an "interruption ended" notice, as Unity's own keyboard code does). The camera's
/// recorder can leave the session as a phone call's, or switched off, and then the coach's voice and the game's sounds
/// are silent and a video on the sound clock never moves.
static void PuttrRestoreSound(void)
{
    AVAudioSession *session = [AVAudioSession sharedInstance];
    [session setCategory:AVAudioSessionCategoryPlayback withOptions:AVAudioSessionCategoryOptionMixWithOthers error:nil];
    [session setActive:YES error:nil];
    [[NSNotificationCenter defaultCenter] postNotificationName:AVAudioSessionInterruptionNotification object:session
        userInfo:@{AVAudioSessionInterruptionTypeKey: @(AVAudioSessionInterruptionTypeEnded)}];
}

@implementation PuttrLabGuide

- (instancetype)initWithFrame:(CGRect)frame
{
    self = [super initWithFrame:frame];
    self.userInteractionEnabled = NO;
    self.opaque = NO;
    self.backgroundColor = UIColor.clearColor;
    self.contentMode = UIViewContentModeRedraw;
    self.card = [[UIView alloc] initWithFrame:CGRectZero];
    self.card.backgroundColor = [UIColor colorWithWhite:0 alpha:0.7];
    self.card.layer.cornerRadius = 12;
    self.card.layer.borderWidth = 1;
    self.card.layer.borderColor = [UIColor colorWithWhite:1 alpha:0.1].CGColor;
    self.label = [[UILabel alloc] initWithFrame:CGRectZero];
    self.label.numberOfLines = 0;
    self.label.textAlignment = NSTextAlignmentCenter;
    self.label.textColor = UIColor.whiteColor;
    self.label.font = [UIFont systemFontOfSize:14 weight:UIFontWeightSemibold];
    [self.card addSubview:self.label];
    self.track = [[UIView alloc] initWithFrame:CGRectZero];
    self.track.backgroundColor = [UIColor colorWithWhite:1 alpha:0.15];
    self.track.layer.cornerRadius = 2;
    self.track.clipsToBounds = YES;
    self.fill = [[UIView alloc] initWithFrame:CGRectZero];
    self.fill.backgroundColor = GuideGreen(1);
    [self.track addSubview:self.fill];
    [self.card addSubview:self.track];
    self.card.hidden = YES;
    [self addSubview:self.card];
    return self;
}

/// A putt was recorded: a card in the middle of the picture for two seconds (flat, as the app's cards are).
- (void)showNote:(NSString *)title body:(NSString *)body good:(BOOL)good
{
    if (self.note == nil)
    {
        self.note = [[UIView alloc] initWithFrame:CGRectZero];
        self.note.backgroundColor = [UIColor colorWithRed:10 / 255.0 green:12 / 255.0 blue:13 / 255.0 alpha:0.94];
        self.note.layer.cornerRadius = 4;
        self.note.layer.borderWidth = 2;
        self.noteTitle = [[UILabel alloc] initWithFrame:CGRectZero];
        self.noteTitle.textAlignment = NSTextAlignmentCenter;
        self.noteTitle.textColor = UIColor.whiteColor;
        self.noteTitle.numberOfLines = 0;
        UIFontDescriptor *heavy = [[UIFont systemFontOfSize:24 weight:UIFontWeightHeavy].fontDescriptor fontDescriptorWithSymbolicTraits:UIFontDescriptorTraitItalic | UIFontDescriptorTraitBold];
        self.noteTitle.font = heavy != nil ? [UIFont fontWithDescriptor:heavy size:24] : [UIFont systemFontOfSize:24 weight:UIFontWeightHeavy];
        self.noteBody = [[UILabel alloc] initWithFrame:CGRectZero];
        self.noteBody.textAlignment = NSTextAlignmentCenter;
        self.noteBody.textColor = [UIColor colorWithRed:196 / 255.0 green:201 / 255.0 blue:205 / 255.0 alpha:1];
        self.noteBody.numberOfLines = 0;
        self.noteBody.font = [UIFont systemFontOfSize:15 weight:UIFontWeightSemibold];
        [self.note addSubview:self.noteTitle];
        [self.note addSubview:self.noteBody];
        self.note.hidden = YES;
        [self addSubview:self.note];
    }
    self.noteTitle.text = title.uppercaseString;
    self.noteBody.text = body;
    self.note.layer.borderColor = (good ? GuideGreen(1) : GuideRed(1)).CGColor;
    self.note.hidden = title.length == 0;
    [self setNeedsLayout];
    NSInteger run = ++self.noteRun;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(2.2 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        if (run == self.noteRun) self.note.hidden = YES;
    });
}

- (void)showLine:(NSString *)text
{
    self.label.text = text;
    self.card.hidden = text.length == 0;
    [self setNeedsLayout];
}

/// Where the picture is in the stage: the stage is filled in Fit mode, so it is the largest rect of the picture's shape.
- (CGRect)pictureRect
{
    CGSize b = self.bounds.size;
    if (b.width <= 0 || b.height <= 0) return CGRectZero;
    CGFloat aspect = self.aspect > 0 ? self.aspect : (b.width > b.height ? 16.0 / 9.0 : 9.0 / 16.0);
    CGFloat w = b.width, h = w / aspect;
    if (h > b.height) { h = b.height; w = h * aspect; }
    return CGRectMake((b.width - w) / 2, (b.height - h) / 2, w, h);
}

- (void)layoutSubviews
{
    [super layoutSubviews];
    CGFloat top = self.window != nil ? self.window.safeAreaInsets.top : 0;
    CGFloat maxWidth = MIN(300, self.bounds.size.width - 48);
    CGSize text = [self.label sizeThatFits:CGSizeMake(maxWidth, CGFLOAT_MAX)];
    BOOL bar = self.progress > 0 && !self.confirmed;
    CGFloat w = MIN(maxWidth, MAX(text.width, bar ? 140 : 0)) + 28, h = text.height + 16 + (bar ? 11 : 0);
    self.card.frame = CGRectMake((self.bounds.size.width - w) / 2, top + 14, w, h);
    self.label.frame = CGRectMake(14, 8, w - 28, text.height);
    self.track.hidden = !bar;
    self.track.frame = CGRectMake(14, 8 + text.height + 7, w - 28, 4);
    self.fill.frame = CGRectMake(0, 0, (w - 28) * MAX(0, MIN(1, self.progress)), 4);
    self.card.layer.borderColor = (self.confirmed ? GuideGreen(0.5) : [UIColor colorWithWhite:1 alpha:0.1]).CGColor;
    if (self.note != nil && !self.note.hidden)
    {
        CGFloat nw = MIN(320, self.bounds.size.width - 48);
        CGSize t = [self.noteTitle sizeThatFits:CGSizeMake(nw - 40, CGFLOAT_MAX)], b = [self.noteBody sizeThatFits:CGSizeMake(nw - 40, CGFLOAT_MAX)];
        CGFloat nh = 20 + t.height + (b.height > 0 ? 8 + b.height : 0) + 20;
        self.note.frame = CGRectMake((self.bounds.size.width - nw) / 2, (self.bounds.size.height - nh) / 2, nw, nh);
        self.noteTitle.frame = CGRectMake(20, 20, nw - 40, t.height);
        self.noteBody.frame = CGRectMake(20, 20 + t.height + 8, nw - 40, b.height);
    }
}

- (void)drawRect:(CGRect)rect
{
    CGRect picture = [self pictureRect];
    if (CGRectIsEmpty(picture)) return;
    CGContextRef c = UIGraphicsGetCurrentContext();

    // The dashed centre line: the ball, the Puttr and the hole sit on it down the line.
    CGContextSaveGState(c);
    CGContextSetStrokeColorWithColor(c, GuideGreen(1).CGColor);
    CGContextSetLineWidth(c, 2);
    CGFloat dash[] = { 8, 8 };
    CGContextSetLineDash(c, 0, dash, 2);
    CGContextMoveToPoint(c, CGRectGetMidX(picture), CGRectGetMinY(picture));
    CGContextAddLineToPoint(c, CGRectGetMidX(picture), CGRectGetMaxY(picture));
    CGContextStrokePath(c);
    CGContextRestoreGState(c);

    // The frame around the picture: red no Player, amber part of them, green all of them (none once held).
    if (self.status != nil && !self.confirmed)
    {
        CGContextSetStrokeColorWithColor(c, [self.status colorWithAlphaComponent:0.9].CGColor);
        CGContextSetLineWidth(c, 3);
        CGContextStrokeRect(c, CGRectInset(picture, 1.5, 1.5));
    }

    // The live skeleton (PoseOverlay.js): joints the detector is sure of in green, the rest in red.
    NSDictionary *lm = self.landmarks;
    if (lm.count == 0) return;
    CGPoint (^at)(NSDictionary *) = ^CGPoint(NSDictionary *p) {
        CGFloat x = [p[@"x"] doubleValue], y = [p[@"y"] doubleValue];
        if (self.mirror) x = 1 - x;
        return CGPointMake(picture.origin.x + x * picture.size.width, picture.origin.y + y * picture.size.height);
    };
    CGFloat (^conf)(NSDictionary *) = ^CGFloat(NSDictionary *p) { return [p[@"inFrame"] doubleValue]; };
    NSArray *bones = @[
        @[@"left_shoulder", @"right_shoulder"], @[@"left_shoulder", @"left_hip"], @[@"right_shoulder", @"right_hip"], @[@"left_hip", @"right_hip"],
        @[@"left_shoulder", @"left_elbow"], @[@"left_elbow", @"left_wrist"], @[@"right_shoulder", @"right_elbow"], @[@"right_elbow", @"right_wrist"],
        @[@"left_hip", @"left_knee"], @[@"left_knee", @"left_ankle"], @[@"right_hip", @"right_knee"], @[@"right_knee", @"right_ankle"],
        @[@"nose", @"left_eye"], @[@"nose", @"right_eye"], @[@"left_eye", @"left_ear"], @[@"right_eye", @"right_ear"],
    ];
    CGContextSetLineWidth(c, 2);
    for (NSArray *bone in bones)
    {
        NSDictionary *a = lm[bone[0]], *b = lm[bone[1]];
        if (a == nil || b == nil || conf(a) < 0.15 || conf(b) < 0.15) continue;
        BOOL strong = conf(a) >= 0.5 && conf(b) >= 0.5;
        CGContextSetStrokeColorWithColor(c, (strong ? GuideGreen(0.9) : GuideRed(0.5)).CGColor);
        CGPoint pa = at(a), pb = at(b);
        CGContextMoveToPoint(c, pa.x, pa.y);
        CGContextAddLineToPoint(c, pb.x, pb.y);
        CGContextStrokePath(c);
    }
    for (NSString *name in lm)
    {
        NSDictionary *p = lm[name];
        if (conf(p) < 0.15) continue;
        CGPoint q = at(p);
        CGRect dot = CGRectMake(q.x - 3.5, q.y - 3.5, 7, 7);
        CGContextSetFillColorWithColor(c, (conf(p) >= 0.5 ? GuideGreen(1) : GuideRed(1)).CGColor);
        CGContextFillEllipseInRect(c, dot);
        CGContextSetStrokeColorWithColor(c, [UIColor colorWithWhite:1 alpha:0.6].CGColor);
        CGContextSetLineWidth(c, 0.75);
        CGContextStrokeEllipseInRect(c, dot);
    }
}

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
    // Ask for the camera first (the system prompt), so the preview does not start on a camera it may not use.
    AVAuthorizationStatus access = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
    if (access == AVAuthorizationStatusNotDetermined)
    {
        [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted) {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (granted) [self startWithAppId:appId token:token channel:channel uid:uid lab:lab];
                else [self send:@"denied"];
            });
        }];
        return;
    }
    if (access == AVAuthorizationStatusDenied || access == AVAuthorizationStatusRestricted) { [self send:@"denied"]; return; }

    if (self.engine == nil) self.engine = [AgoraRtcEngineKit sharedEngineWithAppId:appId delegate:self];
    self.engine.delegate = self;
    [self.engine enableVideo];
    self.lab = lab;
    if (lab)
    {
        // As LabVideoCapture: portrait 1080x1920, 30 fps, 4 Mbps (at least 1.5), frame rate kept over quality; no sound.
        AgoraVideoEncoderConfiguration *config = [[AgoraVideoEncoderConfiguration alloc] initWithSize:CGSizeMake(1080, 1920) frameRate:AgoraVideoFrameRateFps30 bitrate:4000 orientationMode:AgoraVideoOutputOrientationModeFixedPortrait mirrorMode:AgoraVideoMirrorModeAuto];
        config.minBitrate = 1500;
        config.degradationPreference = AgoraDegradationMaintainFramerate;
        [self.engine setVideoEncoderConfiguration:config];
        [self.engine disableAudio];
    }
    else if (self.silent)
    {
        // A round: picture only. The microphone is never opened and nothing from the channel is played.
        [self.engine disableAudio];
        [self.engine enableLocalAudio:NO];
        [self.engine muteLocalAudioStream:YES];
        [self.engine muteAllRemoteAudioStreams:YES];
    }
    else [self.engine enableAudio];
    // With no sound to send (a 3D session, a round), Agora leaves the phone's audio session alone, so the app's own
    // sound carries on through the speaker: the coach's voice in a 3D session, the game's sounds in a round. Left to
    // itself it switches the session to a call's (play and record, the earpiece) and the app goes quiet.
    [self.engine setAudioSessionOperationRestriction:(lab || self.silent) ? AgoraAudioSessionOperationRestrictionAll : AgoraAudioSessionOperationRestrictionNone];
    if (lab || self.silent) PuttrRestoreSound();   // in case an earlier call left the session as a call's
    [self.engine setChannelProfile:AgoraChannelProfileCommunication];
    [self.engine leaveChannel:nil]; // a round left a moment ago may not have let go yet

    if (self.stage == nil)
    {
        self.stage = [[UIView alloc] initWithFrame:CGRectMake(0, 0, 90, 160)];
        self.stage.userInteractionEnabled = NO;
        self.stage.backgroundColor = UIColor.blackColor;
        self.stage.hidden = YES;
        self.preview = [[UIView alloc] initWithFrame:self.stage.bounds];
        self.preview.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
        self.preview.userInteractionEnabled = NO;
        [self.stage addSubview:self.preview];
        self.guide = [[PuttrLabGuide alloc] initWithFrame:self.stage.bounds];
        self.guide.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
        [self.stage addSubview:self.guide];
    }
    // A round's picture is a small rounded tile; a 3D session's fills the screen, with the guide over it.
    self.stage.layer.cornerRadius = lab ? 0 : 12;
    self.stage.layer.masksToBounds = YES;
    self.stage.layer.borderWidth = lab ? 0 : 1;
    self.stage.layer.borderColor = [UIColor colorWithWhite:1 alpha:0.25].CGColor;
    self.guide.hidden = !lab;
    self.guide.landmarks = nil;
    self.guide.aspect = 0;
    self.guide.status = nil;
    self.guide.confirmed = NO;
    self.guide.progress = 0;
    [self.guide showLine:nil];
    [self.guide setNeedsDisplay];
    self.front = YES; // Agora opens the front lens first

    UIView *host = UnityGetGLView();
    if (self.stage.superview != host) [host addSubview:self.stage];
    if (self.remote.superview == host) [host insertSubview:self.remote belowSubview:self.stage];
    [self layout];

    AgoraRtcVideoCanvas *canvas = [AgoraRtcVideoCanvas new];
    canvas.uid = 0;
    canvas.view = self.preview;
    canvas.renderMode = lab ? AgoraVideoRenderModeFit : AgoraVideoRenderModeHidden;
    [self.engine setupLocalVideo:canvas];
    [self.engine startPreview];

    AgoraRtcChannelMediaOptions *options = [AgoraRtcChannelMediaOptions new];
    options.clientRoleType = AgoraClientRoleBroadcaster;
    options.publishCameraTrack = YES;
    options.publishMicrophoneTrack = !lab && !self.silent;
    int result = [self.engine joinChannelByToken:token channelId:channel uid:uid mediaOptions:options joinSuccess:nil];
    if (result != 0) [self send:[NSString stringWithFormat:@"error|%d", result]];
}

/// Where the Player sees themselves: a share of the screen, measured from the top left. Zero width hides it.
/// Kept, so a Place made before the camera has started (or while it is away) applies once it is up.
- (void)placeX:(float)x y:(float)y width:(float)w height:(float)h
{
    self.share = CGRectMake(x, y, w, h);
    [self layout];
}

- (void)layout
{
    if (self.stage == nil || self.stage.superview == nil) return;
    CGSize size = self.stage.superview.bounds.size;
    CGRect s = self.share;
    self.stage.hidden = s.size.width <= 0 || s.size.height <= 0;
    self.stage.frame = CGRectMake(s.origin.x * size.width, s.origin.y * size.height, s.size.width * size.width, s.size.height * size.height);
    [self.guide setNeedsLayout];
    [self.guide setNeedsDisplay];
}

/// The 3D session's guide: the coach's line (empty for none), the frame's colour (alpha 0 for none),
/// the hold-still progress and whether the framing is confirmed.
- (void)guideLine:(NSString *)line red:(float)r green:(float)g blue:(float)b alpha:(float)a progress:(float)progress confirmed:(BOOL)confirmed
{
    if (self.guide == nil) return;
    self.guide.status = a > 0 ? [UIColor colorWithRed:r green:g blue:b alpha:1] : nil;
    self.guide.progress = progress;
    self.guide.confirmed = confirmed;
    [self.guide showLine:line];
    [self.guide setNeedsDisplay];
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
    if (self.remote.superview != host) { if (self.stage.superview == host) [host insertSubview:self.remote belowSubview:self.stage]; else [host addSubview:self.remote]; }
    CGSize size = host.bounds.size;
    self.remote.hidden = w <= 0 || h <= 0;
    self.remote.frame = CGRectMake(x * size.width, y * size.height, w * size.width, h * size.height);
}

- (void)mute:(BOOL)muted { [self.engine muteLocalAudioStream:muted]; }

- (void)switchCamera
{
    if ([self.engine switchCamera] == 0) self.front = !self.front;
    self.guide.mirror = self.front;
    self.guide.landmarks = nil; // the old skeleton belongs to the other lens
    [self.guide setNeedsDisplay];
}

/// The frame around the preview: the framing check's colour (red, amber, green), or none.
- (void)borderRed:(float)r green:(float)g blue:(float)b width:(float)w
{
    if (self.lab) return; // a 3D session's frame is the guide's
    self.stage.layer.borderColor = [UIColor colorWithRed:r green:g blue:b alpha:1].CGColor;
    self.stage.layer.borderWidth = w;
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
    if (![handler performRequests:@[request] error:&error] || request.results.count == 0)
    {
        dispatch_async(dispatch_get_main_queue(), ^{ self.guide.landmarks = nil; [self.guide setNeedsDisplay]; });
        [self send:@"pose|none"];
        return;
    }
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
    NSDictionary *seen = [landmarks copy];
    dispatch_async(dispatch_get_main_queue(), ^{
        // The skeleton over the picture is what this still found.
        self.guide.landmarks = seen;
        if (width > 0 && height > 0) self.guide.aspect = (CGFloat)width / (CGFloat)height;
        self.guide.mirror = self.front;
        [self.guide setNeedsDisplay];
    });
    NSData *json = [NSJSONSerialization dataWithJSONObject:@{ @"width": @(width), @"height": @(height), @"landmarks": landmarks } options:0 error:nil];
    [self send:[@"pose|" stringByAppendingString:[[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]]];
}

- (void)stop
{
    [self.engine leaveChannel:nil];
    [self.engine stopPreview];
    // Once the recorder has let go, the app's sound comes back (a moment later: leaving is not instant).
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.6 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{ PuttrRestoreSound(); });
    [self.stage removeFromSuperview];
    self.stage.hidden = YES;
    self.guide.landmarks = nil;
    [self.guide showLine:nil];
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
    if (uid != 0) return;
    if (size.width > 0 && size.height > 0 && self.guide.landmarks == nil) { self.guide.aspect = size.width / size.height; [self.guide setNeedsDisplay]; }
    [self send:[NSString stringWithFormat:@"size|%d|%d", (int)size.width, (int)size.height]];
}

@end

void PuttrCapture_Start(const char *appId, const char *token, const char *channel, int uid, const char *target, int lab)
{
    NSString *app = [NSString stringWithUTF8String:appId ?: ""], *tok = [NSString stringWithUTF8String:token ?: ""];
    NSString *chan = [NSString stringWithUTF8String:channel ?: ""], *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrCapture.shared.target = to;
        // lab: 1 a 3D session (its own framing), 2 a round with no sound, 0 a live call with sound.
        PuttrCapture.shared.silent = lab != 0;
        [PuttrCapture.shared startWithAppId:app token:tok channel:chan uid:(NSUInteger)uid lab:lab == 1];
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

/// 0 no camera on this device (the Simulator, a Mac), 1 a camera Puttr may use, 2 access refused, 3 not asked yet.
int PuttrCapture_HasCamera(void)
{
    AVCaptureDevice *camera = [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeVideo];
    if (camera == nil) return 0;
    switch ([AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo])
    {
        case AVAuthorizationStatusAuthorized: return 1;
        case AVAuthorizationStatusNotDetermined: return 3;
        default: return 2;
    }
}

void PuttrCapture_Guide(const char *line, float r, float g, float b, float a, float progress, int confirmed)
{
    NSString *text = [NSString stringWithUTF8String:line ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared guideLine:text red:r green:g blue:b alpha:a progress:progress confirmed:confirmed != 0]; });
}

void PuttrCapture_Note(const char *title, const char *body, int good)
{
    NSString *t = [NSString stringWithUTF8String:title ?: ""], *b = [NSString stringWithUTF8String:body ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared.guide showNote:t body:b good:good != 0]; });
}

void PuttrCapture_Mute(int muted) { dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared mute:muted != 0]; }); }

void PuttrCapture_Stop(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrCapture.shared stop]; });
}
