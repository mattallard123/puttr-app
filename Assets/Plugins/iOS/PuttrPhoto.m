// Picking a photo from the library on iOS (puttr-app ProfilePhotoPicker: library only, no camera).
// PHPicker needs no photo permission. The chosen image is written as a JPEG to the temporary folder
// and its path is told to Unity: UnitySendMessage(target, "OnPhoto", path), or "" if cancelled.
#import <UIKit/UIKit.h>
#import <PhotosUI/PhotosUI.h>

extern void UnitySendMessage(const char *object, const char *method, const char *message);
extern UIViewController *UnityGetGLViewController(void);

@interface PuttrPhoto : NSObject <PHPickerViewControllerDelegate>
@property (nonatomic, copy) NSString *target;
@property (nonatomic, assign) BOOL video;
@end

@implementation PuttrPhoto

+ (instancetype)shared
{
    static PuttrPhoto *one;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ one = [PuttrPhoto new]; });
    return one;
}

- (void)answer:(NSString *)path
{
    NSString *to = self.target ?: @"";
    dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(to.UTF8String, "OnPhoto", (path ?: @"").UTF8String); });
}

- (void)pick
{
    PHPickerConfiguration *config = [[PHPickerConfiguration alloc] init];
    config.filter = self.video ? [PHPickerFilter videosFilter] : [PHPickerFilter imagesFilter];
    config.selectionLimit = 1;
    PHPickerViewController *picker = [[PHPickerViewController alloc] initWithConfiguration:config];
    picker.delegate = self;
    [UnityGetGLViewController() presentViewController:picker animated:YES completion:nil];
}

- (void)picker:(PHPickerViewController *)picker didFinishPicking:(NSArray<PHPickerResult *> *)results
{
    [picker dismissViewControllerAnimated:YES completion:nil];
    NSItemProvider *item = results.firstObject.itemProvider;
    if (self.video)
    {
        // A video: copied out of the library to a temp file (the picker's copy is gone when the handler returns).
        if (item == nil || ![item hasItemConformingToTypeIdentifier:@"public.movie"]) { [self answer:@""]; return; }
        [item loadFileRepresentationForTypeIdentifier:@"public.movie" completionHandler:^(NSURL *url, NSError *error) {
            if (url == nil) { [self answer:@""]; return; }
            NSString *path = [NSTemporaryDirectory() stringByAppendingPathComponent:[@"puttr-video." stringByAppendingString:url.pathExtension.length ? url.pathExtension : @"mp4"]];
            [[NSFileManager defaultManager] removeItemAtPath:path error:nil];
            if (![[NSFileManager defaultManager] copyItemAtPath:url.path toPath:path error:nil]) { [self answer:@""]; return; }
            [self answer:path];
        }];
        return;
    }
    if (item == nil || ![item canLoadObjectOfClass:[UIImage class]]) { [self answer:@""]; return; }
    [item loadObjectOfClass:[UIImage class] completionHandler:^(id<NSItemProviderReading> object, NSError *error) {
        UIImage *image = (UIImage *)object;
        if (image == nil) { [self answer:@""]; return; }
        // Drawn upright (the photo's orientation applied), at most 1024 on a side; Unity shrinks it further.
        CGFloat scale = MIN(1.0, 1024.0 / MAX(image.size.width, image.size.height));
        CGSize size = CGSizeMake(round(image.size.width * scale), round(image.size.height * scale));
        UIGraphicsImageRenderer *renderer = [[UIGraphicsImageRenderer alloc] initWithSize:size];
        UIImage *drawn = [renderer imageWithActions:^(UIGraphicsImageRendererContext *ctx) { [image drawInRect:CGRectMake(0, 0, size.width, size.height)]; }];
        NSData *jpg = UIImageJPEGRepresentation(drawn, 0.95);
        NSString *path = [NSTemporaryDirectory() stringByAppendingPathComponent:@"puttr-photo.jpg"];
        if (![jpg writeToFile:path atomically:YES]) { [self answer:@""]; return; }
        [self answer:path];
    }];
}

@end

void PuttrPhoto_PickKind(const char *target, int video)
{
    NSString *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrPhoto.shared.target = to;
        PuttrPhoto.shared.video = video != 0;
        [PuttrPhoto.shared pick];
    });
}

void PuttrPhoto_Pick(const char *target)
{
    NSString *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrPhoto.shared.target = to;
        PuttrPhoto.shared.video = NO;
        [PuttrPhoto.shared pick];
    });
}

// Sharing a file (a Player's data export): the system share sheet, anchored mid-screen for iPad.
void PuttrShare_File(const char *path)
{
    NSString *file = [NSString stringWithUTF8String:path ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController *top = UnityGetGLViewController();
        while (top.presentedViewController) top = top.presentedViewController;
        UIActivityViewController *share = [[UIActivityViewController alloc] initWithActivityItems:@[[NSURL fileURLWithPath:file]] applicationActivities:nil];
        share.popoverPresentationController.sourceView = top.view;
        share.popoverPresentationController.sourceRect = CGRectMake(CGRectGetMidX(top.view.bounds), CGRectGetMidY(top.view.bounds), 1, 1);
        [top presentViewController:share animated:YES completion:nil];
    });
}

// A photo from the front camera (CrewPicker takePhoto: launchCamera, cameraType front): the system camera,
// written as a JPEG; the path goes to Unity as OnPhoto, "" if cancelled or there is no camera.
@interface PuttrCameraTaker : NSObject <UIImagePickerControllerDelegate, UINavigationControllerDelegate>
@property (nonatomic, copy) NSString *target;
@end

@implementation PuttrCameraTaker
- (void)send:(NSString *)path { NSString *to = self.target; dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(to.UTF8String, "OnPhoto", (path ?: @"").UTF8String); }); }
- (void)imagePickerControllerDidCancel:(UIImagePickerController *)picker { [picker dismissViewControllerAnimated:YES completion:nil]; [self send:@""]; }
- (void)imagePickerController:(UIImagePickerController *)picker didFinishPickingMediaWithInfo:(NSDictionary<UIImagePickerControllerInfoKey, id> *)info
{
    [picker dismissViewControllerAnimated:YES completion:nil];
    UIImage *image = info[UIImagePickerControllerOriginalImage];
    if (image == nil) { [self send:@""]; return; }
    CGFloat scale = MIN(1.0, 1024.0 / MAX(image.size.width, image.size.height));
    CGSize size = CGSizeMake(round(image.size.width * scale), round(image.size.height * scale));
    UIImage *drawn = [[[UIGraphicsImageRenderer alloc] initWithSize:size] imageWithActions:^(UIGraphicsImageRendererContext *ctx) { [image drawInRect:CGRectMake(0, 0, size.width, size.height)]; }];
    NSString *path = [NSTemporaryDirectory() stringByAppendingPathComponent:@"puttr-camera.jpg"];
    [self send:[UIImageJPEGRepresentation(drawn, 0.95) writeToFile:path atomically:YES] ? path : @""];
}
@end

static PuttrCameraTaker *puttrCameraTaker;

void PuttrPhoto_Camera(const char *target)
{
    NSString *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        if (puttrCameraTaker == nil) puttrCameraTaker = [PuttrCameraTaker new];
        puttrCameraTaker.target = to;
        if (![UIImagePickerController isSourceTypeAvailable:UIImagePickerControllerSourceTypeCamera]) { [puttrCameraTaker send:@"nocamera"]; return; }
        UIImagePickerController *picker = [UIImagePickerController new];
        picker.sourceType = UIImagePickerControllerSourceTypeCamera;
        if ([UIImagePickerController isCameraDeviceAvailable:UIImagePickerControllerCameraDeviceFront]) picker.cameraDevice = UIImagePickerControllerCameraDeviceFront;
        picker.delegate = puttrCameraTaker;
        UIViewController *top = UnityGetGLViewController();
        while (top.presentedViewController) top = top.presentedViewController;
        [top presentViewController:picker animated:YES completion:nil];
    });
}
