// iOS native share sheet for Prompt World. Presents UIActivityViewController
// with the given message so players can share a stage to any app. Called from
// NativeShare.cs via _pwNativeShare.
#import <UIKit/UIKit.h>

extern "C" void _pwNativeShare(const char *message) {
    NSString *text = message ? [NSString stringWithUTF8String:message] : @"";

    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController *root =
            UnityGetGLViewController() ?: [[[UIApplication sharedApplication] keyWindow] rootViewController];
        if (!root) return;

        UIActivityViewController *vc =
            [[UIActivityViewController alloc] initWithActivityItems:@[text] applicationActivities:nil];

        // iPad requires a popover anchor.
        if (vc.popoverPresentationController) {
            vc.popoverPresentationController.sourceView = root.view;
            vc.popoverPresentationController.sourceRect =
                CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height / 2, 0, 0);
            vc.popoverPresentationController.permittedArrowDirections = 0;
        }
        [root presentViewController:vc animated:YES completion:nil];
    });
}
