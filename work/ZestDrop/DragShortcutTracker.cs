using System;

namespace ZestDrop;

internal enum DragMenuRequest{None,Convert,Tools}

internal sealed class DragShortcutTracker(int horizontalThreshold,int verticalThreshold)
{
    private bool down,candidate,moved,triggered,suppressed;
    private int startX,startY,previousChord;
    public void Cancel()=>suppressed=true;
    public DragMenuRequest Update(bool mouseDown,bool shift,bool control,bool alt,int x,int y,bool shellSource)
    {
        int chord=shift&&!alt?(control?2:1):0;
        if(!mouseDown)
        {
            down=candidate=moved=triggered=suppressed=false;previousChord=chord;return DragMenuRequest.None;
        }
        if(!down){down=true;candidate=shellSource;startX=x;startY=y;previousChord=chord;}
        moved|=Math.Abs((long)x-startX)>=Math.Max(2,horizontalThreshold)||Math.Abs((long)y-startY)>=Math.Max(2,verticalThreshold);
        DragMenuRequest request=DragMenuRequest.None;
        if(candidate&&moved&&!suppressed&&chord!=0)
        {
            if(!triggered||chord==2&&previousChord!=2||chord==1&&previousChord==0)
            {triggered=true;request=chord==2?DragMenuRequest.Tools:DragMenuRequest.Convert;}
        }
        previousChord=chord;return request;
    }
}
