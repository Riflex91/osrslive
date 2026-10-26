// Minimap layout follows the scene renderer described by OpenOSRS Decimator.drawObject.
// Source reference: open-osrs/runelite@915fb55c0a2a1c000dc04a431e68f3bbb29a40cf
import java.awt.image.BufferedImage;
import net.runelite.cache.ObjectManager;
import net.runelite.cache.definitions.SpriteDefinition;
import net.runelite.cache.region.Region;
import net.runelite.cache.region.Location;

public final class NativeObjects {
  private final ObjectManager objects; private final SpriteDefinition[] sprites;
  public NativeObjects(ObjectManager objects,SpriteDefinition[] sprites){this.objects=objects;this.sprites=sprites;}
  private static void pixel(BufferedImage image,int x,int y,int color){if(x>=0&&y>=0&&x<image.getWidth()&&y<image.getHeight())image.setRGB(x,y,color);}
  private static void edge(BufferedImage image,int x,int y,int direction,int color){
    for(int i=0;i<4;i++)switch(direction&3){
      case 0:pixel(image,x,y+i,color);break;case 1:pixel(image,x+i,y,color);break;
      case 2:pixel(image,x+3,y+i,color);break;case 3:pixel(image,x+i,y+3,color);break;
    }
  }
  public void draw(BufferedImage image,Region region,int plane,int minX,int maxY){
    for(Location loc:region.getLocations()){
      int localX=loc.getPosition().getX()-region.getBaseX(),localY=loc.getPosition().getY()-region.getBaseY();
      int rawPlane=loc.getPosition().getZ();
      int effective=rawPlane-((region.getTileSetting(1,localX,localY)&2)!=0?1:0);
      if(effective!=plane&&!(effective==plane+1&&(region.getTileSetting(rawPlane,localX,localY)&8)!=0))continue;
      if((region.getTileSetting(rawPlane,localX,localY)&24)!=0&&effective==plane)continue;
      int type=loc.getType(),orientation=loc.getOrientation();
      if(!(type<=3||type>=9&&type<=22))continue;
      var obj=objects.getObject(loc.getId());if(obj==null)continue;
      int x=(loc.getPosition().getX()-minX)*4,y=(maxY-1-loc.getPosition().getY())*4;
      int scene=obj.getMapSceneID();
      if(scene>=0&&scene<sprites.length){
        var sprite=sprites[scene];
        int sx=x+(obj.getSizeX()*4-sprite.getWidth())/2+sprite.getOffsetX();
        int sy=(maxY-loc.getPosition().getY()-obj.getSizeY())*4+
          (obj.getSizeY()*4-sprite.getHeight())/2+sprite.getOffsetY();
        int[] colors=sprite.getPixels();
        for(int py=0;py<sprite.getHeight();py++)for(int px=0;px<sprite.getWidth();px++){
          int rgb=colors[py*sprite.getWidth()+px];if(rgb!=0)pixel(image,sx+px,sy+py,rgb);
        }
      }else if(type<=3){
        int color=obj.getWallOrDoor()==0?0xEEEEEE:0xEE0000;
        if(type==0||type==2)edge(image,x,y,orientation,color);
        if(type==2)edge(image,x,y,orientation+1,color);
        if(type==3){int[] dx={0,3,3,0},dy={0,0,3,3};pixel(image,x+dx[orientation&3],y+dy[orientation&3],color);}
      }else if(type==9){
        int color=obj.getWallOrDoor()==0?0xEEEEEE:0xEE0000;
        for(int i=0;i<4;i++)pixel(image,x+i,y+((orientation&1)==0?3-i:i),color);
      }
    }
  }
}
