
#import <UIKit/UIKit.h>
#import "NativeAlerts.h"

extern void UnitySendMessage(const char*, const char*, const char*);

static UIViewController* TopVC(void) {
    UIWindow *keyWindow = UIApplication.sharedApplication.keyWindow;
    if (!keyWindow) {
        for (UIWindow *w in UIApplication.sharedApplication.windows) {
            if (w.isKeyWindow) { keyWindow = w; break; }
        }
    }
    UIViewController *root = keyWindow.rootViewController;
    while (root.presentedViewController) { root = root.presentedViewController; }
    return root;
}

static void Resolve(int callbackId, NSInteger index) {
    NSString *payload = [NSString stringWithFormat:@"%d|%ld", callbackId, (long)index];
    UnitySendMessage("NativeAlertBridge_GO", "OnAlertResult", payload.UTF8String);
}

void _na_showAlert(const char* json, int callbackId)
{
    if (!json) { Resolve(callbackId, 0); return; }

    NSString *jsonStr = [NSString stringWithUTF8String:json];
    NSData *data = [jsonStr dataUsingEncoding:NSUTF8StringEncoding];
    NSError *err = nil;
    NSDictionary *obj = [NSJSONSerialization JSONObjectWithData:data options:0 error:&err];
    if (err || ![obj isKindOfClass:[NSDictionary class]]) { Resolve(callbackId, 0); return; }

    NSString *title = obj[@"title"] ?: @"";
    NSString *message = obj[@"message"] ?: @"";
    NSString *themeStr = obj[@"theme"] ?: @"System";
    NSArray  *buttons = obj[@"buttons"] ?: @[ @{@"text":@"OK",@"style":@"Default"} ];

    if (buttons.count > 3) {
        buttons = [buttons subarrayWithRange:NSMakeRange(0, 3)];
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        UIAlertController *ac = [UIAlertController alertControllerWithTitle:title.length?title:nil
                                                                    message:message.length?message:nil
                                                             preferredStyle:UIAlertControllerStyleAlert];
        if (@available(iOS 13.0, *)) {
            if ([themeStr isEqualToString:@"Light"]) ac.overrideUserInterfaceStyle = UIUserInterfaceStyleLight;
            else if ([themeStr isEqualToString:@"Dark"]) ac.overrideUserInterfaceStyle = UIUserInterfaceStyleDark;
        }

        [buttons enumerateObjectsUsingBlock:^(NSDictionary* btn, NSUInteger idx, BOOL *stop) {
            NSString *text = btn[@"text"] ?: @"OK";
            NSString *style = btn[@"style"] ?: @"Default";
            UIAlertActionStyle aa = UIAlertActionStyleDefault;
            if ([style isEqualToString:@"Cancel"]) aa = UIAlertActionStyleCancel;
            else if ([style isEqualToString:@"Destructive"]) aa = UIAlertActionStyleDestructive;

            UIAlertAction *action = [UIAlertAction actionWithTitle:text style:aa handler:^(UIAlertAction *a){
                Resolve(callbackId, (NSInteger)idx);
            }];
            [ac addAction:action];
        }];

        UIViewController *presenter = TopVC();
        if (presenter) [presenter presentViewController:ac animated:YES completion:nil];
        else Resolve(callbackId, 0);
    });
}
