import java.io.File;
import java.io.IOException;
import java.awt.image.BufferedImage;
import javax.imageio.ImageIO;
import net.runelite.cache.IndexType;
import net.runelite.cache.MapImageDumper;
import net.runelite.cache.ObjectManager;
import net.runelite.cache.definitions.loaders.SpriteLoader;
import net.runelite.cache.fs.Store;
import net.runelite.cache.region.RegionLoader;

public class BuildMinimapAtlas {
  public static void main(String[] args) throws Exception {
    int minRx=Integer.parseInt(args[2]),minRy=Integer.parseInt(args[3]);
    int maxRx=Integer.parseInt(args[4]),maxRy=Integer.parseInt(args[5]);
    var output=new File(args[1]);output.mkdirs();
    try(Store store=new Store(new File(args[0]))){
      store.load();
      if(store.getIndex(IndexType.MAPS).isNamed())throw new IOException("Modern unencrypted map cache required");
      var loader=new RegionLoader(store,id->null){
        @Override public void loadRegions() throws IOException {
          for(int x=minRx-1;x<=maxRx+1;x++)for(int y=minRy-1;y<=maxRy+1;y++){
            try{loadRegionFromArchive((x<<8)|y);}catch(IOException ex){
              if(x>=minRx&&x<=maxRx&&y>=minRy&&y<=maxRy)throw ex;
            }
          }
          calculateBounds();
        }
      };
      var terrain=new MapImageDumper(store,loader).setRenderObjects(false).setRenderIcons(false).setRenderLabels(false).setLowMemory(false);
      terrain.load();
      var objects=new ObjectManager(store);objects.load();
      var archive=store.getIndex(IndexType.SPRITES).findArchiveByName("mapscene");
      var sprites=new SpriteLoader().load(archive.getArchiveId(),archive.decompress(store.getStorage().loadArchive(archive)));
      var renderer=new NativeObjects(objects,sprites);
      for(int plane=0;plane<4;plane++){
        var image=new BufferedImage((maxRx-minRx+1)*256,(maxRy-minRy+1)*256,BufferedImage.TYPE_INT_RGB);
        var g=image.createGraphics();
        for(int x=minRx;x<=maxRx;x++)for(int y=minRy;y<=maxRy;y++){
          var region=loader.findRegionForRegionCoordinates(x,y);
          if(region==null)throw new IOException("Incomplete region "+x+","+y);
          g.drawImage(terrain.drawRegion(region,plane),(x-minRx)*256,(maxRy-y)*256,null);
        }
        g.dispose();
        for(var region:loader.getRegions())renderer.draw(image,region,plane,minRx*64,(maxRy+1)*64);
        ImageIO.write(image,"png",new File(output,"plane-"+plane+".png"));
        System.out.println("ATLAS plane="+plane+" min_x="+minRx*64+" max_y="+(maxRy+1)*64+" pixels="+image.getWidth()+"x"+image.getHeight());
      }
    }
  }
}
