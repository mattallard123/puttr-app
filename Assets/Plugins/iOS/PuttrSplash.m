// A spinner on the launch screen while the engine starts (Matt: on a first install the app showed only the Puttr
// logo for several seconds and looked frozen). The launch storyboard cannot animate, so a system spinner is laid
// over it, under the logo, as soon as the app's window exists; Unity removes it (PuttrSplash_Hide) once its own
// loader, with the moving bar, is on screen.
#import <UIKit/UIKit.h>

static UIActivityIndicatorView *gSpin;
static BOOL gDone;

@interface PuttrSplash : NSObject
@end

@implementation PuttrSplash

+ (void)load
{
    [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidFinishLaunchingNotification object:nil queue:[NSOperationQueue mainQueue] usingBlock:^(NSNotification *note) {
        [PuttrSplash showTry:0];
    }];
}

+ (UIWindow *)window
{
    UIWindow *found = nil;
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (![scene isKindOfClass:UIWindowScene.class]) continue;
        for (UIWindow *w in ((UIWindowScene *)scene).windows)
        {
            if (w.isKeyWindow) return w;
            if (found == nil) found = w;
        }
    }
    return found;
}

+ (void)showTry:(int)attempt
{
    if (gDone || gSpin != nil) return;
    UIWindow *window = [self window];
    if (window == nil)
    {
        // The scene's window comes a moment after launch (UIScene life cycle): look again shortly.
        if (attempt < 50) dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.1 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{ [PuttrSplash showTry:attempt + 1]; });
        return;
    }
    gSpin = [[UIActivityIndicatorView alloc] initWithActivityIndicatorStyle:UIActivityIndicatorViewStyleMedium];
    gSpin.color = [UIColor colorWithWhite:1.0 alpha:0.55];
    gSpin.hidesWhenStopped = YES;
    // Under the logo: the splash logo is half the short side wide and 1943:479 (Loader.SplashShare/SplashAspect).
    CGSize size = window.bounds.size;
    CGFloat shortSide = MIN(size.width, size.height);
    CGFloat logoHigh = shortSide * 0.5 / (1943.0 / 479.0);
    gSpin.center = CGPointMake(size.width * 0.5, size.height * 0.5 + logoHigh * 1.9);
    gSpin.autoresizingMask = UIViewAutoresizingFlexibleLeftMargin | UIViewAutoresizingFlexibleRightMargin | UIViewAutoresizingFlexibleTopMargin | UIViewAutoresizingFlexibleBottomMargin;
    [window addSubview:gSpin];
    [gSpin startAnimating];
}

@end

void PuttrSplash_Hide(void)
{
    gDone = YES;
    if (gSpin == nil) return;
    [gSpin stopAnimating];
    [gSpin removeFromSuperview];
    gSpin = nil;
}
