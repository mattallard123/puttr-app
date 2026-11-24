// Sign in with Apple, and a web sign-in sheet (used for Google), for the Mac app.
// The Mac twin of Assets/Plugins/iOS/PuttrSignIn.m. A Mac plug-in is a bundle Unity loads, with no
// UnitySendMessage to call, so (as PuttrBle) what happens is queued as a line of text and Unity polls:
//
//   apple \t identity token (a JWT) for Firebase
//   web   \t the callback URL the web sign-in ended on
//   error \t why it stopped; "cancelled" when the Player closed the sheet
//
// Build with build.sh in this folder; the result goes to Assets/Plugins/macOS.
#import <AppKit/AppKit.h>
#import <AuthenticationServices/AuthenticationServices.h>

@interface PuttrSignInMac : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding, ASWebAuthenticationPresentationContextProviding>
@property (nonatomic, strong) NSMutableArray<NSString *> *lines;
@property (nonatomic, strong) ASWebAuthenticationSession *session;
@property (nonatomic, strong) ASAuthorizationController *controller;
@end

@implementation PuttrSignInMac

+ (instancetype)shared
{
    static PuttrSignInMac *one;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ one = [PuttrSignInMac new]; one.lines = [NSMutableArray new]; });
    return one;
}

- (void)say:(NSString *)kind text:(NSString *)text
{
    @synchronized (self.lines) { [self.lines addObject:[NSString stringWithFormat:@"%@\t%@", kind, text ?: @""]]; }
}

- (NSString *)next
{
    @synchronized (self.lines)
    {
        if (self.lines.count == 0) return nil;
        NSString *line = self.lines.firstObject;
        [self.lines removeObjectAtIndex:0];
        return line;
    }
}

- (NSWindow *)window { return NSApp.keyWindow ?: NSApp.mainWindow ?: NSApp.windows.firstObject; }

// ── Apple ────────────────────────────────────────────────────────────────

- (void)appleWithNonce:(NSString *)hashedNonce
{
    ASAuthorizationAppleIDRequest *request = [[ASAuthorizationAppleIDProvider new] createRequest];
    request.requestedScopes = @[ASAuthorizationScopeEmail, ASAuthorizationScopeFullName];
    request.nonce = hashedNonce;
    self.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
    self.controller.delegate = self;
    self.controller.presentationContextProvider = self;
    [self.controller performRequests];
}

- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithAuthorization:(ASAuthorization *)authorization
{
    self.controller = nil;
    if (![authorization.credential isKindOfClass:ASAuthorizationAppleIDCredential.class]) { [self say:@"error" text:@"Apple gave no credential."]; return; }
    ASAuthorizationAppleIDCredential *credential = (ASAuthorizationAppleIDCredential *)authorization.credential;
    NSString *token = credential.identityToken ? [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding] : nil;
    if (token.length == 0) { [self say:@"error" text:@"Apple gave no identity token."]; return; }
    [self say:@"apple" text:token];
}

- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithError:(NSError *)error
{
    self.controller = nil;
    BOOL cancelled = [error.domain isEqualToString:ASAuthorizationErrorDomain] && error.code == ASAuthorizationErrorCanceled;
    [self say:@"error" text:cancelled ? @"cancelled" : error.localizedDescription];
}

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller { return [self window]; }

// ── A web sign-in sheet ──────────────────────────────────────────────────

- (void)webAuth:(NSString *)url scheme:(NSString *)scheme
{
    __weak PuttrSignInMac *weak = self;
    self.session = [[ASWebAuthenticationSession alloc] initWithURL:[NSURL URLWithString:url] callbackURLScheme:scheme completionHandler:^(NSURL *callback, NSError *error) {
        PuttrSignInMac *me = weak;
        me.session = nil;
        if (callback != nil) { [me say:@"web" text:callback.absoluteString]; return; }
        BOOL cancelled = [error.domain isEqualToString:ASWebAuthenticationSessionErrorDomain] && error.code == ASWebAuthenticationSessionErrorCodeCanceledLogin;
        [me say:@"error" text:cancelled ? @"cancelled" : (error.localizedDescription ?: @"Sign-in did not finish.")];
    }];
    self.session.presentationContextProvider = self;
    if (![self.session start]) { self.session = nil; [self say:@"error" text:@"Could not open the sign-in page."]; }
}

- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession *)session { return [self window]; }

@end

void PuttrSignInMac_Apple(const char *hashedNonce)
{
    NSString *nonce = [NSString stringWithUTF8String:hashedNonce ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrSignInMac.shared appleWithNonce:nonce]; });
}

void PuttrSignInMac_WebAuth(const char *url, const char *scheme)
{
    NSString *address = [NSString stringWithUTF8String:url ?: ""], *callback = [NSString stringWithUTF8String:scheme ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrSignInMac.shared webAuth:address scheme:callback]; });
}

// The next line as UTF-8 into buffer; its length, or 0 when there is none.
int PuttrSignInMac_Poll(char *buffer, int capacity)
{
    NSString *line = [PuttrSignInMac.shared next];
    if (line == nil || buffer == NULL || capacity <= 0) return 0;
    NSData *data = [line dataUsingEncoding:NSUTF8StringEncoding];
    int n = (int)MIN((NSUInteger)capacity, data.length);
    memcpy(buffer, data.bytes, n);
    return n;
}
