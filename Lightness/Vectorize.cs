using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lightness.Renderer;
using PrettyPrinter;

namespace Lightness {
	public class Vectorize {
		readonly Pixel[] Pixels;
		readonly int Width, Height;
		
		public Vectorize(Pixel[] pixels, int width, int height) {
			Pixels = pixels;
			Width = width;
			Height = height;
			
			FindDepthDelta();
			RemoveNonEdges();
			FloodFill();
			var paths = Trace();
			FinalPaths = ReorderPaths(paths);
		}

		static readonly (int, int)[] Neighbors = {
			(-1, -1), (0, -1), (1, -1), 
			(-1, 0), /*home*/ (1, 0), 
			(-1, 1), (0, 1), (1, 1)
		};
		
		Pixel sample(int x, int y) => x < 0 || x >= Width || y < 0 || y >= Height ? null : Pixels[y * Width + x];
		IEnumerable<Pixel> sampleNeighbors(int x, int y) =>
			Neighbors.Select(t => sample(x + t.Item1, y + t.Item2));

		void FindDepthDelta() {
			"Finding depth deltas".Print();
			for(int y = 0, i = 0; y < Height; ++y)
				for(var x = 0; x < Width; ++x, ++i) {
					var pixel = Pixels[i];
					if(pixel == null) continue;

					var neighborDeltas = sampleNeighbors(x, y).Select(n => n == null ? pixel.Depth : MathF.Abs(pixel.Depth - n.Depth));
					pixel.DepthDelta = neighborDeltas.Max();
					pixel.Edge = pixel.DepthDelta > 0.00001f;
				}
		}

		void RemoveNonEdges() {
			for(var i = 0; i < Pixels.Length; ++i)
				if(Pixels[i] != null && !Pixels[i].Edge)
					Pixels[i] = null;
		}
		
		readonly List<List<(int, int)>> Patches = new List<List<(int, int)>>();

		void Flood(int sx, int sy) {
			var locs = new List<(int, int)>();
			Patches.Add(locs);
			var queue = new Queue<(float, int, int)>();
			queue.Enqueue((Pixels[sy * Width + sx].Depth, sx, sy));

			while(queue.Count != 0) {
				var (pdepth, x, y) = queue.Dequeue();
				var i = y * Width + x;
				if(x < 0 || x >= Width || y < 0 || y >= Height || Pixels[i] == null || Pixels[i].Flooded) continue;
				var depth = Pixels[i].Depth;
				if(MathF.Abs(pdepth - depth) > 1) continue;
				locs.Add((x, y));
				Pixels[i].Flooded = true;
				queue.Enqueue((depth, x - 1, y - 1));
				queue.Enqueue((depth, x    , y - 1));
				queue.Enqueue((depth, x + 1, y - 1));
				queue.Enqueue((depth, x - 1, y    ));
				queue.Enqueue((depth, x + 1, y    ));
				queue.Enqueue((depth, x - 1, y + 1));
				queue.Enqueue((depth, x    , y + 1));
				queue.Enqueue((depth, x + 1, y + 1));
			}
		}

		void FloodFill() {
			"Flood filling".Print();
			for(int y = 0, i = 0; y < Height; ++y)
				for(var x = 0; x < Width; ++x, ++i)
					if(Pixels[i] != null && !Pixels[i].Flooded)
						Flood(x, y);
		}

		List<((int, int), (int, int))> TracePatch(List<(int, int)> patch) {
			var pixels = new bool[Width * Height];
			foreach(var (x, y) in patch)
				pixels[y * Width + x] = true;

			bool Check(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height && pixels[y * Width + x];
			void Erase(int x, int y) {
				if(x >= 0 && x < Width && y >= 0 && y < Height) pixels[y * Width + x] = false;
			}
			int Count(int x, int y) {
				var sum = 0;
				for(var i = -1; i <= 1; ++i)
					for(var j = -1; j <= 1; ++j)
						sum += Check(x + i, y + j) ? 1 : 0;
				return sum;
			}
			
			$"Tracing patch of {patch.Count} pixels".Print();
			
			var lines = new List<((int, int), (int, int))>();
			var queue = new Queue<((int, int), (int, int))>();
			queue.Enqueue((patch[0], (-1, -1)));
			while(queue.Count > 0) {
				var ((x, y), (px, py)) = queue.Dequeue();
				if(Count(x, y) == 0) continue;
				if(px != -1 && py != -1) lines.Add(((px, py), (x, y)));
				Erase(x, y);
				foreach(var (nx, ny) in Neighbors)
					Erase(x + nx, y + ny);

				var opts = new List<(int, int, int)>();
				foreach(var (nx, ny) in Neighbors) {
					var n = Count(x + nx, y + ny);
					if(n != 0) opts.Add((x + nx, y + ny, n));
				}
				foreach(var (nx, ny, _) in opts.OrderByDescending(v => v.Item3))
					queue.Enqueue(((nx, ny), (x, y)));
			}
			
			return lines;
		}

		List<List<(int, int)>> Pathify(List<((int, int), (int, int))> lines) {
			var paths = new List<List<(int, int)>>();
			foreach(var (a, b) in lines) {
				var found = false;
				foreach(var path in paths) {
					if(path.Contains(a) && path.Contains(b)) continue;
					var end = path.Last();
					if(end == a || end == b) {
						path.Add(end == b ? a : b);
						found = true;
						break;
					}
					var start = path[0];
					if(start == a || start == b) {
						path.Add(start == b ? a : b);
						found = true;
						break;
					}
				}
				if(!found)
					paths.Add(new List<(int, int)> { a, b });
			}
			return paths;
		}

		List<List<(int, int)>> Trace() {
			"Tracing patches".Print();
			var lines = new List<((int, int), (int, int))>();
			foreach(var patch in Patches)
				lines.AddRange(TracePatch(patch));
			"Lines to paths".Print();
			return Pathify(lines);
		}

		float Dist((int, int) a, (int, int) b) {
			var (c, d) = (b.Item1 - a.Item1, b.Item2 - a.Item2);
			return MathF.Sqrt(c * c + d * d);
		}

		List<List<(int, int)>> ReorderPaths(List<List<(int, int)>> paths) {
			"Reordering paths".Print();
			var last = paths[0].Last();
			var npaths = new List<List<(int, int)>> { paths[0] };
			var remaining = paths.Skip(1).ToList();
			while(remaining.Count != 0) {
				(float, List<(int, int)>, bool) closest = (float.PositiveInfinity, null, false);
				foreach(var elem in remaining) {
					var sd = Dist(last, elem[0]);
					var ed = Dist(last, elem.Last());
					if(sd <= ed && sd < closest.Item1) {
						closest = (sd, elem, false);
						if(sd == 0) break;
					}
					else if(ed < sd && ed < closest.Item1) {
						closest = (ed, elem, true);
						if(ed == 0) break;
					}
				}
				var cpath = closest.Item2;
				remaining.Remove(cpath);
				if(closest.Item3) cpath.Reverse();
				last = cpath.Last();
				npaths.Add(cpath);
			}
			return paths;
		}

		List<List<(int, int)>> FinalPaths;

		public void Output(string fn) {
			"Writing SVG".Print();
			using(var fp = File.Open(fn, FileMode.Create, FileAccess.Write))
				using(var sw = new StreamWriter(fp)) {
					sw.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
					sw.WriteLine("<svg baseProfile=\"tiny\" height=\"100%\" version=\"1.2\" width=\"100%\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:ev=\"http://www.w3.org/2001/xml-events\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><defs />");
					foreach(var path in FinalPaths) {
						var (fx, fy) = path[0];
						sw.Write($"<path d=\"M {fx / 8f} {fy / 8f}");
						foreach(var (nx, ny) in path.Skip(1))
							sw.Write($" L {nx / 8f} {ny / 8f}");
						sw.WriteLine("\" fill=\"red\" fill-opacity=\"0\" stroke=\"black\" stroke-width=\"1\" />");
					}
					sw.WriteLine("</svg>");
				}
		}
	}
}