using System;

namespace RailgunSandbox
{
    public struct ApproachAcceleration
    {
        public double X, Y, Z;
        public double Magnitude => Math.Sqrt(X*X+Y*Y+Z*Z);
    }

    public static class PoweredApproach
    {
        // Cubic position intercept with specified final velocity. These are
        // requested engine accelerations; native attitude, thrust and fuel
        // remain authoritative. Y is local up; gravity acts along -Y.
        public static ApproachAcceleration Command(double x,double height,double z,double vx,double vy,double vz,double gravity,double time)
        {
            return new ApproachAcceleration {
                X=6*x/(time*time)-4*vx/time,
                Y=-6*height/(time*time)-(4*vy-1)/time+gravity,
                Z=6*z/(time*time)-4*vz/time
            };
        }

        public static double Plan(double x,double height,double z,double vx,double vy,double vz,double gravity,double available,
            double axisX=0,double axisY=1,double axisZ=0,double maximumInitialTurn=180)
        {
            foreach(double value in new[]{x,height,z,vx,vy,vz,gravity,available,axisX,axisY,axisZ,maximumInitialTurn})
                if(!FlightRules.Finite(value))return double.NaN;
            double axisMagnitude=Math.Sqrt(axisX*axisX+axisY*axisY+axisZ*axisZ);
            if(height<30||height>7000||gravity<=0||available<=gravity*1.1||vy>20||axisMagnitude<.1||maximumInitialTurn<0||maximumInitialTurn>180)return double.NaN;
            double best=double.NaN,cost=double.PositiveInfinity;
            for(double time=6;time<=120;time+=.5) {
                var start=Command(x,height,z,vx,vy,vz,gravity,time);
                // Dense-air steering is not an instantaneous free-vector turn.
                // Reject a schedule whose first burn needs a large axis change.
                if(start.Magnitude<=0||(start.X*axisX+start.Y*axisY+start.Z*axisZ)/(start.Magnitude*axisMagnitude)<Math.Cos(maximumInitialTurn*Math.PI/180))continue;
                // Cubic terminal acceleration at vf=(0,-.5,0).
                var end=new ApproachAcceleration { X=-6*x/(time*time)+2*vx/time,
                    Y=6*height/(time*time)+(2*vy-2)/time+gravity,
                    Z=-6*z/(time*time)+2*vz/time };
                bool feasible=true;double dv=0;
                for(int i=0;i<=20;i++) {
                    double f=i/20.0;
                    var a=new ApproachAcceleration {X=start.X+(end.X-start.X)*f,Y=start.Y+(end.Y-start.Y)*f,Z=start.Z+(end.Z-start.Z)*f};
                    if(a.Y<=0||a.Magnitude>available*.85||Math.Sqrt(a.X*a.X+a.Z*a.Z)>a.Y*Math.Tan(Math.PI/3)) {feasible=false;break;}
                    double t=time*f;
                    double altitude=height+vy*t+(start.Y-gravity)*t*t/2+(end.Y-start.Y)*t*t*t/(6*time);
                    if(i<20&&altitude<0) {feasible=false;break;}
                    dv+=a.Magnitude*(i==0||i==20?.5:1)*time/20;
                }
                if(feasible&&dv<cost) {cost=dv;best=time;}
            }
            return best;
        }
    }
}
