// Recording the coach's lesson in the app (puttr-app LessonRecording: ReplayKit in-app, app plus microphone).
// ReplayKit captures only this app and the microphone, so notifications stay private. Stopping writes the take
// to the path Unity gave (RPScreenRecorder stopRecordingWithOutputURL), and Unity puts it straight into Deliver's
// lesson-video slot: no preview, no camera roll, no re-picking.
//
// The coach-camera bubble is ReplayKit's own front-camera preview (cameraPreviewView), added to Unity's view in the
// top-left corner. It is in-app UI, so the capture records it; the coach can drag it out of the way mid-take.
//
// What happens is told to Unity with UnitySendMessage(target, "OnRecord", ...):
//   started                 the take is live
//   stopped|<path>          the file is written
//   failed|<reason>         it could not start or the file could not be written
//   nomic                   the microphone is not allowed (the coach's voice is the lesson)
//   cancelled               a take was thrown away
#import <UIKit/UIKit.h>
#import <ReplayKit/ReplayKit.h>
#import <AVFoundation/AVFoundation.h>

extern void UnitySendMessage(const char *object, const char *method, const char *message);
extern UIView *UnityGetGLView(void);

static NSString *puttrRecordTarget = @"PuttrRecord";
static NSString *puttrRecordPath = nil;
static UIView *puttrRecordCamera = nil;

static void PuttrRecordSend(NSString *message)
{
    NSString *target = puttrRecordTarget;
    dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(target.UTF8String, "OnRecord", message.UTF8String); });
}

/// Drags the camera bubble: it is the coach's face, so they park it where it hides nothing they are explaining.
@interface PuttrRecordDrag : NSObject
- (void)pan:(UIPanGestureRecognizer *)pan;
@end

@implementation PuttrRecordDrag
- (void)pan:(UIPanGestureRecognizer *)pan
{
    UIView *view = pan.view;
    UIView *host = view.superview;
    if (view == nil || host == nil) return;
    CGPoint move = [pan translationInView:host];
    CGPoint centre = CGPointMake(view.center.x + move.x, view.center.y + move.y);
    CGFloat halfW = view.bounds.size.width / 2, halfH = view.bounds.size.height / 2;
    centre.x = MAX(halfW, MIN(host.bounds.size.width - halfW, centre.x));
    centre.y = MAX(halfH, MIN(host.bounds.size.height - halfH, centre.y));
    view.center = centre;
    [pan setTranslation:CGPointZero inView:host];
}
@end

static PuttrRecordDrag *puttrRecordDrag;

static void PuttrRecordHideCamera(void)
{
    if (puttrRecordCamera != nil) [puttrRecordCamera removeFromSuperview];
    puttrRecordCamera = nil;
}

static void PuttrRecordShowCamera(void)
{
    UIView *preview = RPScreenRecorder.sharedRecorder.cameraPreviewView;
    UIView *host = UnityGetGLView();
    if (preview == nil || host == nil) return;
    // The legacy bubble's size and place: 104 x 138 points, 16 in from the left, 70 below the safe area.
    preview.frame = CGRectMake(16, host.safeAreaInsets.top + 70, 104, 138);
    preview.layer.cornerRadius = 14;
    preview.layer.masksToBounds = YES;
    preview.layer.borderWidth = 2;
    preview.layer.borderColor = [UIColor whiteColor].CGColor;
    preview.userInteractionEnabled = YES;
    if (puttrRecordDrag == nil) puttrRecordDrag = [PuttrRecordDrag new];
    BOOL dragging = NO;
    for (UIGestureRecognizer *g in preview.gestureRecognizers) if ([g isKindOfClass:[UIPanGestureRecognizer class]]) dragging = YES;
    if (!dragging) [preview addGestureRecognizer:[[UIPanGestureRecognizer alloc] initWithTarget:puttrRecordDrag action:@selector(pan:)]];
    [host addSubview:preview];
    puttrRecordCamera = preview;
}

static void PuttrRecordStartNow(BOOL camera)
{
    RPScreenRecorder *recorder = RPScreenRecorder.sharedRecorder;
    recorder.microphoneEnabled = YES;
    recorder.cameraEnabled = camera;
    if (camera) recorder.cameraPosition = RPCameraPositionFront;
    [recorder startRecordingWithHandler:^(NSError *error) {
        if (error != nil)
        {
            NSLog(@"Puttr record: %@", error);
            PuttrRecordSend([@"failed|" stringByAppendingString:error.localizedDescription ?: @"ReplayKit could not start."]);
            return;
        }
        dispatch_async(dispatch_get_main_queue(), ^{
            if (camera) PuttrRecordShowCamera();
            PuttrRecordSend(@"started");
        });
    }];
}

int PuttrRecord_Available(void) { return RPScreenRecorder.sharedRecorder.isAvailable ? 1 : 0; }
int PuttrRecord_Recording(void) { return RPScreenRecorder.sharedRecorder.isRecording ? 1 : 0; }

int PuttrRecord_CameraAvailable(void)
{
    AVCaptureDevice *front = [AVCaptureDevice defaultDeviceWithDeviceType:AVCaptureDeviceTypeBuiltInWideAngleCamera mediaType:AVMediaTypeVideo position:AVCaptureDevicePositionFront];
    return front != nil ? 1 : 0;
}

/// Starts a take that will be written to path. The microphone is asked for first (no voice, no lesson); the
/// camera only when the coach chose the bubble, and a refused camera records without it rather than not at all.
void PuttrRecord_Begin(const char *target, const char *path, int camera)
{
    if (target != NULL) puttrRecordTarget = [NSString stringWithUTF8String:target];
    puttrRecordPath = path != NULL ? [NSString stringWithUTF8String:path] : nil;
    BOOL withCamera = camera != 0;
    dispatch_async(dispatch_get_main_queue(), ^{
        RPScreenRecorder *recorder = RPScreenRecorder.sharedRecorder;
        if (!recorder.isAvailable) { PuttrRecordSend(@"failed|Screen recording is not available on this device right now."); return; }
        if (recorder.isRecording) { PuttrRecordSend(@"started"); return; }
        [[AVAudioSession sharedInstance] requestRecordPermission:^(BOOL granted) {
            if (!granted) { PuttrRecordSend(@"nomic"); return; }
            if (!withCamera) { dispatch_async(dispatch_get_main_queue(), ^{ PuttrRecordStartNow(NO); }); return; }
            [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL allowed) {
                dispatch_async(dispatch_get_main_queue(), ^{ PuttrRecordStartNow(allowed); });
            }];
        }];
    });
}

/// Stops the take and writes it to the path Begin was given.
void PuttrRecord_End(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrRecordHideCamera();
        RPScreenRecorder *recorder = RPScreenRecorder.sharedRecorder;
        NSString *path = puttrRecordPath;
        if (!recorder.isRecording || path == nil) { PuttrRecordSend(@"failed|Nothing was recording."); return; }
        [[NSFileManager defaultManager] removeItemAtPath:path error:nil];
        if (@available(iOS 14.0, *))
        {
            [recorder stopRecordingWithOutputURL:[NSURL fileURLWithPath:path] completionHandler:^(NSError *error) {
                recorder.cameraEnabled = NO;
                if (error != nil)
                {
                    NSLog(@"Puttr record stop: %@", error);
                    PuttrRecordSend(@"failed|The recording could not be saved.");
                    return;
                }
                PuttrRecordSend([@"stopped|" stringByAppendingString:path]);
            }];
        }
        else
        {
            // Before iOS 14 ReplayKit only hands back its preview, which this flow does not use.
            [recorder stopRecordingWithHandler:^(RPPreviewViewController *preview, NSError *error) {
                PuttrRecordSend(@"failed|Recording in the app needs iOS 14 or later.");
            }];
        }
    });
}

/// Throws a take away (the coach left, or the app is signing out).
void PuttrRecord_Cancel(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrRecordHideCamera();
        RPScreenRecorder *recorder = RPScreenRecorder.sharedRecorder;
        if (!recorder.isRecording) return;
        [recorder stopRecordingWithHandler:^(RPPreviewViewController *preview, NSError *error) {
            recorder.cameraEnabled = NO;
            if (preview != nil) [preview dismissViewControllerAnimated:NO completion:nil];
            PuttrRecordSend(@"cancelled");
        }];
    });
}
