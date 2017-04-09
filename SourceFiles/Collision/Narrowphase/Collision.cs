﻿/*
* Velcro Physics:
* Copyright (c) 2017 Ian Qvist
* 
* Original source Box2D:
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org 
* 
* This software is provided 'as-is', without any express or implied 
* warranty.  In no event will the authors be held liable for any damages 
* arising from the use of this software. 
* Permission is granted to anyone to use this software for any purpose, 
* including commercial applications, and to alter it and redistribute it 
* freely, subject to the following restrictions: 
* 1. The origin of this software must not be misrepresented; you must not 
* claim that you wrote the original software. If you use this software 
* in a product, an acknowledgment in the product documentation would be 
* appreciated but is not required. 
* 2. Altered source versions must be plainly marked as such, and must not be 
* misrepresented as being the original software. 
* 3. This notice may not be removed or altered from any source distribution. 
*/

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using VelcroPhysics.Collision.ContactSystem;
using VelcroPhysics.Collision.Shapes;
using VelcroPhysics.Primitives;
using VelcroPhysics.Primitives.Optimization;
using VelcroPhysics.Utils;

namespace VelcroPhysics.Collision.Narrowphase
{
    /// <summary>
    /// Collision methods
    /// </summary>
    public static class Collision
    {
        [ThreadStatic]
        private static DistanceInput _input;

        /// <summary>
        /// Test overlap between the two shapes.
        /// </summary>
        /// <param name="shapeA">The first shape.</param>
        /// <param name="indexA">The index for the first shape.</param>
        /// <param name="shapeB">The second shape.</param>
        /// <param name="indexB">The index for the second shape.</param>
        /// <param name="xfA">The transform for the first shape.</param>
        /// <param name="xfB">The transform for the seconds shape.</param>
        /// <returns></returns>
        public static bool TestOverlap(Shape shapeA, int indexA, Shape shapeB, int indexB, ref Transform xfA, ref Transform xfB)
        {
            _input = _input ?? new DistanceInput();
            _input.ProxyA.Set(shapeA, indexA);
            _input.ProxyB.Set(shapeB, indexB);
            _input.TransformA = xfA;
            _input.TransformB = xfB;
            _input.UseRadii = true;

            SimplexCache cache;
            DistanceOutput output;
            Distance.ComputeDistance(out output, out cache, _input);

            return output.Distance < 10.0f * Settings.Epsilon;
        }

        public static void GetPointStates(out FixedArray2<PointState> state1, out FixedArray2<PointState> state2, ref Manifold manifold1, ref Manifold manifold2)
        {
            state1 = new FixedArray2<PointState>();
            state2 = new FixedArray2<PointState>();

            // Detect persists and removes.
            for (int i = 0; i < manifold1.PointCount; ++i)
            {
                ContactID id = manifold1.Points[i].Id;

                state1[i] = PointState.Remove;

                for (int j = 0; j < manifold2.PointCount; ++j)
                {
                    if (manifold2.Points[j].Id.Key == id.Key)
                    {
                        state1[i] = PointState.Persist;
                        break;
                    }
                }
            }

            // Detect persists and adds.
            for (int i = 0; i < manifold2.PointCount; ++i)
            {
                ContactID id = manifold2.Points[i].Id;

                state2[i] = PointState.Add;

                for (int j = 0; j < manifold1.PointCount; ++j)
                {
                    if (manifold1.Points[j].Id.Key == id.Key)
                    {
                        state2[i] = PointState.Persist;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Compute the collision manifold between two polygons.
        /// </summary>
        /// <param name="manifold">The manifold.</param>
        /// <param name="polyA">The poly A.</param>
        /// <param name="transformA">The transform A.</param>
        /// <param name="polyB">The poly B.</param>
        /// <param name="transformB">The transform B.</param>
        public static void CollidePolygons(ref Manifold manifold, PolygonShape polyA, ref Transform transformA, PolygonShape polyB, ref Transform transformB)
        {
            manifold.PointCount = 0;
            float totalRadius = polyA.Radius + polyB.Radius;

            int edgeA = 0;
            float separationA = FindMaxSeparation(out edgeA, polyA, ref transformA, polyB, ref transformB);
            if (separationA > totalRadius)
                return;

            int edgeB = 0;
            float separationB = FindMaxSeparation(out edgeB, polyB, ref transformB, polyA, ref transformA);
            if (separationB > totalRadius)
                return;

            PolygonShape poly1; // reference polygon
            PolygonShape poly2; // incident polygon
            Transform xf1, xf2;
            int edge1; // reference edge
            bool flip;
            const float k_relativeTol = 0.98f;
            const float k_absoluteTol = 0.001f;

            if (separationB > k_relativeTol * separationA + k_absoluteTol)
            {
                poly1 = polyB;
                poly2 = polyA;
                xf1 = transformB;
                xf2 = transformA;
                edge1 = edgeB;
                manifold.Type = ManifoldType.FaceB;
                flip = true;
            }
            else
            {
                poly1 = polyA;
                poly2 = polyB;
                xf1 = transformA;
                xf2 = transformB;
                edge1 = edgeA;
                manifold.Type = ManifoldType.FaceA;
                flip = false;
            }

            FixedArray2<ClipVertex> incidentEdge;
            FindIncidentEdge(out incidentEdge, poly1, ref xf1, edge1, poly2, ref xf2);

            int count1 = poly1.Vertices.Count;

            int iv1 = edge1;
            int iv2 = edge1 + 1 < count1 ? edge1 + 1 : 0;

            Vector2 v11 = poly1.Vertices[iv1];
            Vector2 v12 = poly1.Vertices[iv2];

            Vector2 localTangent = v12 - v11;
            localTangent.Normalize();

            Vector2 localNormal = new Vector2(localTangent.Y, -localTangent.X);
            Vector2 planePoint = 0.5f * (v11 + v12);

            Vector2 tangent = MathUtils.Mul(xf1.q, localTangent);

            float normalx = tangent.Y;
            float normaly = -tangent.X;

            v11 = MathUtils.Mul(ref xf1, v11);
            v12 = MathUtils.Mul(ref xf1, v12);

            // Face offset.
            float frontOffset = normalx * v11.X + normaly * v11.Y;

            // Side offsets, extended by polytope skin thickness.
            float sideOffset1 = -(tangent.X * v11.X + tangent.Y * v11.Y) + totalRadius;
            float sideOffset2 = tangent.X * v12.X + tangent.Y * v12.Y + totalRadius;

            // Clip incident edge against extruded edge1 side edges.
            FixedArray2<ClipVertex> clipPoints1;
            FixedArray2<ClipVertex> clipPoints2;

            // Clip to box side 1
            int np = ClipSegmentToLine(out clipPoints1, ref incidentEdge, -tangent, sideOffset1, iv1);

            if (np < 2)
                return;

            // Clip to negative box side 1
            np = ClipSegmentToLine(out clipPoints2, ref clipPoints1, tangent, sideOffset2, iv2);

            if (np < 2)
            {
                return;
            }

            // Now clipPoints2 contains the clipped points.
            manifold.LocalNormal = localNormal;
            manifold.LocalPoint = planePoint;

            int pointCount = 0;
            for (int i = 0; i < Settings.MaxManifoldPoints; ++i)
            {
                Vector2 value = clipPoints2[i].V;
                float separation = normalx * value.X + normaly * value.Y - frontOffset;

                if (separation <= totalRadius)
                {
                    ManifoldPoint cp = manifold.Points[pointCount];
                    cp.LocalPoint = MathUtils.MulT(ref xf2, clipPoints2[i].V);
                    cp.Id = clipPoints2[i].ID;

                    if (flip)
                    {
                        // Swap features
                        ContactFeature cf = cp.Id.Features;
                        cp.Id.Features.IndexA = cf.IndexB;
                        cp.Id.Features.IndexB = cf.IndexA;
                        cp.Id.Features.TypeA = cf.TypeB;
                        cp.Id.Features.TypeB = cf.TypeA;
                    }

                    manifold.Points[pointCount] = cp;

                    ++pointCount;
                }
            }

            manifold.PointCount = pointCount;
        }

        /// <summary>
        /// Compute contact points for edge versus circle.
        /// This accounts for edge connectivity.
        /// </summary>
        /// <param name="manifold">The manifold.</param>
        /// <param name="edgeA">The edge A.</param>
        /// <param name="transformA">The transform A.</param>
        /// <param name="circleB">The circle B.</param>
        /// <param name="transformB">The transform B.</param>
        public static void CollideEdgeAndCircle(ref Manifold manifold, EdgeShape edgeA, ref Transform transformA, CircleShape circleB, ref Transform transformB)
        {
            manifold.PointCount = 0;

            // Compute circle in frame of edge
            Vector2 Q = MathUtils.MulT(ref transformA, MathUtils.Mul(ref transformB, ref circleB._position));

            Vector2 A = edgeA.Vertex1, B = edgeA.Vertex2;
            Vector2 e = B - A;

            // Barycentric coordinates
            float u = Vector2.Dot(e, B - Q);
            float v = Vector2.Dot(e, Q - A);

            float radius = edgeA.Radius + circleB.Radius;

            ContactFeature cf;
            cf.IndexB = 0;
            cf.TypeB = (byte)ContactFeatureType.Vertex;

            Vector2 P, d;

            // Region A
            if (v <= 0.0f)
            {
                P = A;
                d = Q - P;
                float dd;
                Vector2.Dot(ref d, ref d, out dd);
                if (dd > radius * radius)
                {
                    return;
                }

                // Is there an edge connected to A?
                if (edgeA.HasVertex0)
                {
                    Vector2 A1 = edgeA.Vertex0;
                    Vector2 B1 = A;
                    Vector2 e1 = B1 - A1;
                    float u1 = Vector2.Dot(e1, B1 - Q);

                    // Is the circle in Region AB of the previous edge?
                    if (u1 > 0.0f)
                    {
                        return;
                    }
                }

                cf.IndexA = 0;
                cf.TypeA = (byte)ContactFeatureType.Vertex;
                manifold.PointCount = 1;
                manifold.Type = ManifoldType.Circles;
                manifold.LocalNormal = Vector2.Zero;
                manifold.LocalPoint = P;
                ManifoldPoint mp = new ManifoldPoint();
                mp.Id.Key = 0;
                mp.Id.Features = cf;
                mp.LocalPoint = circleB.Position;
                manifold.Points[0] = mp;
                return;
            }

            // Region B
            if (u <= 0.0f)
            {
                P = B;
                d = Q - P;
                float dd;
                Vector2.Dot(ref d, ref d, out dd);
                if (dd > radius * radius)
                {
                    return;
                }

                // Is there an edge connected to B?
                if (edgeA.HasVertex3)
                {
                    Vector2 B2 = edgeA.Vertex3;
                    Vector2 A2 = B;
                    Vector2 e2 = B2 - A2;
                    float v2 = Vector2.Dot(e2, Q - A2);

                    // Is the circle in Region AB of the next edge?
                    if (v2 > 0.0f)
                    {
                        return;
                    }
                }

                cf.IndexA = 1;
                cf.TypeA = (byte)ContactFeatureType.Vertex;
                manifold.PointCount = 1;
                manifold.Type = ManifoldType.Circles;
                manifold.LocalNormal = Vector2.Zero;
                manifold.LocalPoint = P;
                ManifoldPoint mp = new ManifoldPoint();
                mp.Id.Key = 0;
                mp.Id.Features = cf;
                mp.LocalPoint = circleB.Position;
                manifold.Points[0] = mp;
                return;
            }

            // Region AB
            float den;
            Vector2.Dot(ref e, ref e, out den);
            System.Diagnostics.Debug.Assert(den > 0.0f);
            P = (1.0f / den) * (u * A + v * B);
            d = Q - P;
            float dd2;
            Vector2.Dot(ref d, ref d, out dd2);
            if (dd2 > radius * radius)
            {
                return;
            }

            Vector2 n = new Vector2(-e.Y, e.X);
            if (Vector2.Dot(n, Q - A) < 0.0f)
            {
                n = new Vector2(-n.X, -n.Y);
            }
            n.Normalize();

            cf.IndexA = 0;
            cf.TypeA = (byte)ContactFeatureType.Face;
            manifold.PointCount = 1;
            manifold.Type = ManifoldType.FaceA;
            manifold.LocalNormal = n;
            manifold.LocalPoint = A;
            ManifoldPoint mp2 = new ManifoldPoint();
            mp2.Id.Key = 0;
            mp2.Id.Features = cf;
            mp2.LocalPoint = circleB.Position;
            manifold.Points[0] = mp2;
        }

        /// <summary>
        /// Collides and edge and a polygon, taking into account edge adjacency.
        /// </summary>
        /// <param name="manifold">The manifold.</param>
        /// <param name="edgeA">The edge A.</param>
        /// <param name="xfA">The xf A.</param>
        /// <param name="polygonB">The polygon B.</param>
        /// <param name="xfB">The xf B.</param>
        public static void CollideEdgeAndPolygon(ref Manifold manifold, EdgeShape edgeA, ref Transform xfA, PolygonShape polygonB, ref Transform xfB)
        {
            EPCollider collider = new EPCollider();
            collider.Collide(ref manifold, edgeA, ref xfA, polygonB, ref xfB);
        }

        /// <summary>
        /// Clipping for contact manifolds.
        /// </summary>
        /// <param name="vOut">The v out.</param>
        /// <param name="vIn">The v in.</param>
        /// <param name="normal">The normal.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="vertexIndexA">The vertex index A.</param>
        /// <returns></returns>
        private static int ClipSegmentToLine(out FixedArray2<ClipVertex> vOut, ref FixedArray2<ClipVertex> vIn, Vector2 normal, float offset, int vertexIndexA)
        {
            vOut = new FixedArray2<ClipVertex>();

            ClipVertex v0 = vIn[0];
            ClipVertex v1 = vIn[1];

            // Start with no output points
            int numOut = 0;

            // Calculate the distance of end points to the line
            float distance0 = normal.X * v0.V.X + normal.Y * v0.V.Y - offset;
            float distance1 = normal.X * v1.V.X + normal.Y * v1.V.Y - offset;

            // If the points are behind the plane
            if (distance0 <= 0.0f)
                vOut[numOut++] = v0;
            if (distance1 <= 0.0f)
                vOut[numOut++] = v1;

            // If the points are on different sides of the plane
            if (distance0 * distance1 < 0.0f)
            {
                // Find intersection point of edge and plane
                float interp = distance0 / (distance0 - distance1);

                ClipVertex cv = vOut[numOut];

                cv.V.X = v0.V.X + interp * (v1.V.X - v0.V.X);
                cv.V.Y = v0.V.Y + interp * (v1.V.Y - v0.V.Y);

                // VertexA is hitting edgeB.
                cv.ID.Features.IndexA = (byte)vertexIndexA;
                cv.ID.Features.IndexB = v0.ID.Features.IndexB;
                cv.ID.Features.TypeA = (byte)ContactFeatureType.Vertex;
                cv.ID.Features.TypeB = (byte)ContactFeatureType.Face;

                vOut[numOut] = cv;

                ++numOut;
            }

            return numOut;
        }

        /// <summary>
        /// Find the separation between poly1 and poly2 for a give edge normal on poly1.
        /// </summary>
        /// <param name="poly1">The poly1.</param>
        /// <param name="xf1">The XF1.</param>
        /// <param name="edge1">The edge1.</param>
        /// <param name="poly2">The poly2.</param>
        /// <param name="xf2">The XF2.</param>
        /// <returns></returns>
        private static float EdgeSeparation(PolygonShape poly1, ref Transform xf1, int edge1, PolygonShape poly2, ref Transform xf2)
        {
            List<Vector2> vertices1 = poly1.Vertices;
            List<Vector2> normals1 = poly1.Normals;

            int count2 = poly2.Vertices.Count;
            List<Vector2> vertices2 = poly2.Vertices;

            System.Diagnostics.Debug.Assert(0 <= edge1 && edge1 < poly1.Vertices.Count);

            // Convert normal from poly1's frame into poly2's frame.
            Vector2 normal1World = MathUtils.Mul(xf1.q, normals1[edge1]);
            Vector2 normal1 = MathUtils.MulT(xf2.q, normal1World);

            // Find support vertex on poly2 for -normal.
            int index = 0;
            float minDot = Settings.MaxFloat;

            for (int i = 0; i < count2; ++i)
            {
                float dot = Vector2.Dot(vertices2[i], normal1);
                if (dot < minDot)
                {
                    minDot = dot;
                    index = i;
                }
            }

            Vector2 v1 = MathUtils.Mul(ref xf1, vertices1[edge1]);
            Vector2 v2 = MathUtils.Mul(ref xf2, vertices2[index]);
            float separation = Vector2.Dot(v2 - v1, normal1World);
            return separation;
        }

        /// <summary>
        /// Find the max separation between poly1 and poly2 using edge normals from poly1.
        /// </summary>
        /// <param name="edgeIndex">Index of the edge.</param>
        /// <param name="poly1">The poly1.</param>
        /// <param name="xf1">The XF1.</param>
        /// <param name="poly2">The poly2.</param>
        /// <param name="xf2">The XF2.</param>
        /// <returns></returns>
        private static float FindMaxSeparation(out int edgeIndex, PolygonShape poly1, ref Transform xf1, PolygonShape poly2, ref Transform xf2)
        {
            int count1 = poly1.Vertices.Count;
            List<Vector2> normals1 = poly1.Normals;

            // Vector pointing from the centroid of poly1 to the centroid of poly2.
            Vector2 d = MathUtils.Mul(ref xf2, poly2.MassData.Centroid) - MathUtils.Mul(ref xf1, poly1.MassData.Centroid);
            Vector2 dLocal1 = MathUtils.MulT(xf1.q, d);

            // Find edge normal on poly1 that has the largest projection onto d.
            int edge = 0;
            float maxDot = -Settings.MaxFloat;
            for (int i = 0; i < count1; ++i)
            {
                float dot = Vector2.Dot(normals1[i], dLocal1);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    edge = i;
                }
            }

            // Get the separation for the edge normal.
            float s = EdgeSeparation(poly1, ref xf1, edge, poly2, ref xf2);

            // Check the separation for the previous edge normal.
            int prevEdge = edge - 1 >= 0 ? edge - 1 : count1 - 1;
            float sPrev = EdgeSeparation(poly1, ref xf1, prevEdge, poly2, ref xf2);

            // Check the separation for the next edge normal.
            int nextEdge = edge + 1 < count1 ? edge + 1 : 0;
            float sNext = EdgeSeparation(poly1, ref xf1, nextEdge, poly2, ref xf2);

            // Find the best edge and the search direction.
            int bestEdge;
            float bestSeparation;
            int increment;
            if (sPrev > s && sPrev > sNext)
            {
                increment = -1;
                bestEdge = prevEdge;
                bestSeparation = sPrev;
            }
            else if (sNext > s)
            {
                increment = 1;
                bestEdge = nextEdge;
                bestSeparation = sNext;
            }
            else
            {
                edgeIndex = edge;
                return s;
            }

            // Perform a local search for the best edge normal.
            for (;;)
            {
                if (increment == -1)
                    edge = bestEdge - 1 >= 0 ? bestEdge - 1 : count1 - 1;
                else
                    edge = bestEdge + 1 < count1 ? bestEdge + 1 : 0;

                s = EdgeSeparation(poly1, ref xf1, edge, poly2, ref xf2);

                if (s > bestSeparation)
                {
                    bestEdge = edge;
                    bestSeparation = s;
                }
                else
                {
                    break;
                }
            }

            edgeIndex = bestEdge;
            return bestSeparation;
        }

        private static void FindIncidentEdge(out FixedArray2<ClipVertex> c, PolygonShape poly1, ref Transform xf1, int edge1, PolygonShape poly2, ref Transform xf2)
        {
            c = new FixedArray2<ClipVertex>();
            Vertices normals1 = poly1.Normals;

            int count2 = poly2.Vertices.Count;
            Vertices vertices2 = poly2.Vertices;
            Vertices normals2 = poly2.Normals;

            System.Diagnostics.Debug.Assert(0 <= edge1 && edge1 < poly1.Vertices.Count);

            // Get the normal of the reference edge in poly2's frame.
            Vector2 normal1 = MathUtils.MulT(xf2.q, MathUtils.Mul(xf1.q, normals1[edge1]));

            // Find the incident edge on poly2.
            int index = 0;
            float minDot = Settings.MaxFloat;
            for (int i = 0; i < count2; ++i)
            {
                float dot = Vector2.Dot(normal1, normals2[i]);
                if (dot < minDot)
                {
                    minDot = dot;
                    index = i;
                }
            }

            // Build the clip vertices for the incident edge.
            int i1 = index;
            int i2 = i1 + 1 < count2 ? i1 + 1 : 0;

            ClipVertex cv0 = c[0];

            cv0.V = MathUtils.Mul(ref xf2, vertices2[i1]);
            cv0.ID.Features.IndexA = (byte)edge1;
            cv0.ID.Features.IndexB = (byte)i1;
            cv0.ID.Features.TypeA = (byte)ContactFeatureType.Face;
            cv0.ID.Features.TypeB = (byte)ContactFeatureType.Vertex;

            c[0] = cv0;

            ClipVertex cv1 = c[1];
            cv1.V = MathUtils.Mul(ref xf2, vertices2[i2]);
            cv1.ID.Features.IndexA = (byte)edge1;
            cv1.ID.Features.IndexB = (byte)i2;
            cv1.ID.Features.TypeA = (byte)ContactFeatureType.Face;
            cv1.ID.Features.TypeB = (byte)ContactFeatureType.Vertex;

            c[1] = cv1;
        }

        private class EPCollider
        {
            private Vector2 _centroidB;
            private bool _front;
            private Vector2 _lowerLimit, _upperLimit;
            private Vector2 _normal;
            private Vector2 _normal0, _normal1, _normal2;
            private TempPolygon _polygonB = new TempPolygon();
            private float _radius;
            private Vector2 _v0, _v1, _v2, _v3;

            private Transform _xf;

            public void Collide(ref Manifold manifold, EdgeShape edgeA, ref Transform xfA, PolygonShape polygonB, ref Transform xfB)
            {
                // Algorithm:
                // 1. Classify v1 and v2
                // 2. Classify polygon centroid as front or back
                // 3. Flip normal if necessary
                // 4. Initialize normal range to [-pi, pi] about face normal
                // 5. Adjust normal range according to adjacent edges
                // 6. Visit each separating axes, only accept axes within the range
                // 7. Return if _any_ axis indicates separation
                // 8. Clip

                _xf = MathUtils.MulT(xfA, xfB);

                _centroidB = MathUtils.Mul(ref _xf, polygonB.MassData.Centroid);

                _v0 = edgeA.Vertex0;
                _v1 = edgeA._vertex1;
                _v2 = edgeA._vertex2;
                _v3 = edgeA.Vertex3;

                bool hasVertex0 = edgeA.HasVertex0;
                bool hasVertex3 = edgeA.HasVertex3;

                Vector2 edge1 = _v2 - _v1;
                edge1.Normalize();
                _normal1 = new Vector2(edge1.Y, -edge1.X);
                float offset1 = Vector2.Dot(_normal1, _centroidB - _v1);
                float offset0 = 0.0f, offset2 = 0.0f;
                bool convex1 = false, convex2 = false;

                // Is there a preceding edge?
                if (hasVertex0)
                {
                    Vector2 edge0 = _v1 - _v0;
                    edge0.Normalize();
                    _normal0 = new Vector2(edge0.Y, -edge0.X);
                    convex1 = MathUtils.Cross(edge0, edge1) >= 0.0f;
                    offset0 = Vector2.Dot(_normal0, _centroidB - _v0);
                }

                // Is there a following edge?
                if (hasVertex3)
                {
                    Vector2 edge2 = _v3 - _v2;
                    edge2.Normalize();
                    _normal2 = new Vector2(edge2.Y, -edge2.X);
                    convex2 = MathUtils.Cross(edge1, edge2) > 0.0f;
                    offset2 = Vector2.Dot(_normal2, _centroidB - _v2);
                }

                // Determine front or back collision. Determine collision normal limits.
                if (hasVertex0 && hasVertex3)
                {
                    if (convex1 && convex2)
                    {
                        _front = offset0 >= 0.0f || offset1 >= 0.0f || offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal0;
                            _upperLimit = _normal2;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = -_normal1;
                        }
                    }
                    else if (convex1)
                    {
                        _front = offset0 >= 0.0f || (offset1 >= 0.0f && offset2 >= 0.0f);
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal0;
                            _upperLimit = _normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal2;
                            _upperLimit = -_normal1;
                        }
                    }
                    else if (convex2)
                    {
                        _front = offset2 >= 0.0f || (offset0 >= 0.0f && offset1 >= 0.0f);
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = _normal2;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = -_normal0;
                        }
                    }
                    else
                    {
                        _front = offset0 >= 0.0f && offset1 >= 0.0f && offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = _normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal2;
                            _upperLimit = -_normal0;
                        }
                    }
                }
                else if (hasVertex0)
                {
                    if (convex1)
                    {
                        _front = offset0 >= 0.0f || offset1 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal0;
                            _upperLimit = -_normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = -_normal1;
                        }
                    }
                    else
                    {
                        _front = offset0 >= 0.0f && offset1 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = -_normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = -_normal0;
                        }
                    }
                }
                else if (hasVertex3)
                {
                    if (convex2)
                    {
                        _front = offset1 >= 0.0f || offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = _normal2;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = _normal1;
                        }
                    }
                    else
                    {
                        _front = offset1 >= 0.0f && offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = _normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal2;
                            _upperLimit = _normal1;
                        }
                    }
                }
                else
                {
                    _front = offset1 >= 0.0f;
                    if (_front)
                    {
                        _normal = _normal1;
                        _lowerLimit = -_normal1;
                        _upperLimit = -_normal1;
                    }
                    else
                    {
                        _normal = -_normal1;
                        _lowerLimit = _normal1;
                        _upperLimit = _normal1;
                    }
                }

                // Get polygonB in frameA
                _polygonB.Count = polygonB.Vertices.Count;
                for (int i = 0; i < polygonB.Vertices.Count; ++i)
                {
                    _polygonB.Vertices[i] = MathUtils.Mul(ref _xf, polygonB.Vertices[i]);
                    _polygonB.Normals[i] = MathUtils.Mul(_xf.q, polygonB.Normals[i]);
                }

                _radius = 2.0f * Settings.PolygonRadius;

                manifold.PointCount = 0;

                EPAxis edgeAxis = ComputeEdgeSeparation();

                // If no valid normal can be found than this edge should not collide.
                if (edgeAxis.Type == EPAxisType.Unknown)
                {
                    return;
                }

                if (edgeAxis.Separation > _radius)
                {
                    return;
                }

                EPAxis polygonAxis = ComputePolygonSeparation();
                if (polygonAxis.Type != EPAxisType.Unknown && polygonAxis.Separation > _radius)
                {
                    return;
                }

                // Use hysteresis for jitter reduction.
                const float k_relativeTol = 0.98f;
                const float k_absoluteTol = 0.001f;

                EPAxis primaryAxis;
                if (polygonAxis.Type == EPAxisType.Unknown)
                {
                    primaryAxis = edgeAxis;
                }
                else if (polygonAxis.Separation > k_relativeTol * edgeAxis.Separation + k_absoluteTol)
                {
                    primaryAxis = polygonAxis;
                }
                else
                {
                    primaryAxis = edgeAxis;
                }

                FixedArray2<ClipVertex> ie = new FixedArray2<ClipVertex>();
                ReferenceFace rf;
                if (primaryAxis.Type == EPAxisType.EdgeA)
                {
                    manifold.Type = ManifoldType.FaceA;

                    // Search for the polygon normal that is most anti-parallel to the edge normal.
                    int bestIndex = 0;
                    float bestValue = Vector2.Dot(_normal, _polygonB.Normals[0]);
                    for (int i = 1; i < _polygonB.Count; ++i)
                    {
                        float value = Vector2.Dot(_normal, _polygonB.Normals[i]);
                        if (value < bestValue)
                        {
                            bestValue = value;
                            bestIndex = i;
                        }
                    }

                    int i1 = bestIndex;
                    int i2 = i1 + 1 < _polygonB.Count ? i1 + 1 : 0;

                    ClipVertex c0 = ie[0];
                    c0.V = _polygonB.Vertices[i1];
                    c0.ID.Features.IndexA = 0;
                    c0.ID.Features.IndexB = (byte)i1;
                    c0.ID.Features.TypeA = (byte)ContactFeatureType.Face;
                    c0.ID.Features.TypeB = (byte)ContactFeatureType.Vertex;
                    ie[0] = c0;

                    ClipVertex c1 = ie[1];
                    c1.V = _polygonB.Vertices[i2];
                    c1.ID.Features.IndexA = 0;
                    c1.ID.Features.IndexB = (byte)i2;
                    c1.ID.Features.TypeA = (byte)ContactFeatureType.Face;
                    c1.ID.Features.TypeB = (byte)ContactFeatureType.Vertex;
                    ie[1] = c1;

                    if (_front)
                    {
                        rf.i1 = 0;
                        rf.i2 = 1;
                        rf.v1 = _v1;
                        rf.v2 = _v2;
                        rf.normal = _normal1;
                    }
                    else
                    {
                        rf.i1 = 1;
                        rf.i2 = 0;
                        rf.v1 = _v2;
                        rf.v2 = _v1;
                        rf.normal = -_normal1;
                    }
                }
                else
                {
                    manifold.Type = ManifoldType.FaceB;
                    ClipVertex c0 = ie[0];
                    c0.V = _v1;
                    c0.ID.Features.IndexA = 0;
                    c0.ID.Features.IndexB = (byte)primaryAxis.Index;
                    c0.ID.Features.TypeA = (byte)ContactFeatureType.Vertex;
                    c0.ID.Features.TypeB = (byte)ContactFeatureType.Face;
                    ie[0] = c0;

                    ClipVertex c1 = ie[1];
                    c1.V = _v2;
                    c1.ID.Features.IndexA = 0;
                    c1.ID.Features.IndexB = (byte)primaryAxis.Index;
                    c1.ID.Features.TypeA = (byte)ContactFeatureType.Vertex;
                    c1.ID.Features.TypeB = (byte)ContactFeatureType.Face;
                    ie[1] = c1;

                    rf.i1 = primaryAxis.Index;
                    rf.i2 = rf.i1 + 1 < _polygonB.Count ? rf.i1 + 1 : 0;
                    rf.v1 = _polygonB.Vertices[rf.i1];
                    rf.v2 = _polygonB.Vertices[rf.i2];
                    rf.normal = _polygonB.Normals[rf.i1];
                }

                rf.sideNormal1 = new Vector2(rf.normal.Y, -rf.normal.X);
                rf.sideNormal2 = -rf.sideNormal1;
                rf.sideOffset1 = Vector2.Dot(rf.sideNormal1, rf.v1);
                rf.sideOffset2 = Vector2.Dot(rf.sideNormal2, rf.v2);

                // Clip incident edge against extruded edge1 side edges.
                FixedArray2<ClipVertex> clipPoints1;
                FixedArray2<ClipVertex> clipPoints2;
                int np;

                // Clip to box side 1
                np = ClipSegmentToLine(out clipPoints1, ref ie, rf.sideNormal1, rf.sideOffset1, rf.i1);

                if (np < Settings.MaxManifoldPoints)
                {
                    return;
                }

                // Clip to negative box side 1
                np = ClipSegmentToLine(out clipPoints2, ref clipPoints1, rf.sideNormal2, rf.sideOffset2, rf.i2);

                if (np < Settings.MaxManifoldPoints)
                {
                    return;
                }

                // Now clipPoints2 contains the clipped points.
                if (primaryAxis.Type == EPAxisType.EdgeA)
                {
                    manifold.LocalNormal = rf.normal;
                    manifold.LocalPoint = rf.v1;
                }
                else
                {
                    manifold.LocalNormal = polygonB.Normals[rf.i1];
                    manifold.LocalPoint = polygonB.Vertices[rf.i1];
                }

                int pointCount = 0;
                for (int i = 0; i < Settings.MaxManifoldPoints; ++i)
                {
                    float separation = Vector2.Dot(rf.normal, clipPoints2[i].V - rf.v1);

                    if (separation <= _radius)
                    {
                        ManifoldPoint cp = manifold.Points[pointCount];

                        if (primaryAxis.Type == EPAxisType.EdgeA)
                        {
                            cp.LocalPoint = MathUtils.MulT(ref _xf, clipPoints2[i].V);
                            cp.Id = clipPoints2[i].ID;
                        }
                        else
                        {
                            cp.LocalPoint = clipPoints2[i].V;
                            cp.Id.Features.TypeA = clipPoints2[i].ID.Features.TypeB;
                            cp.Id.Features.TypeB = clipPoints2[i].ID.Features.TypeA;
                            cp.Id.Features.IndexA = clipPoints2[i].ID.Features.IndexB;
                            cp.Id.Features.IndexB = clipPoints2[i].ID.Features.IndexA;
                        }

                        manifold.Points[pointCount] = cp;
                        ++pointCount;
                    }
                }

                manifold.PointCount = pointCount;
            }

            private EPAxis ComputeEdgeSeparation()
            {
                EPAxis axis;
                axis.Type = EPAxisType.EdgeA;
                axis.Index = _front ? 0 : 1;
                axis.Separation = Settings.MaxFloat;

                for (int i = 0; i < _polygonB.Count; ++i)
                {
                    float s = Vector2.Dot(_normal, _polygonB.Vertices[i] - _v1);
                    if (s < axis.Separation)
                    {
                        axis.Separation = s;
                    }
                }

                return axis;
            }

            private EPAxis ComputePolygonSeparation()
            {
                EPAxis axis;
                axis.Type = EPAxisType.Unknown;
                axis.Index = -1;
                axis.Separation = -Settings.MaxFloat;

                Vector2 perp = new Vector2(-_normal.Y, _normal.X);

                for (int i = 0; i < _polygonB.Count; ++i)
                {
                    Vector2 n = -_polygonB.Normals[i];

                    float s1 = Vector2.Dot(n, _polygonB.Vertices[i] - _v1);
                    float s2 = Vector2.Dot(n, _polygonB.Vertices[i] - _v2);
                    float s = Math.Min(s1, s2);

                    if (s > _radius)
                    {
                        // No collision
                        axis.Type = EPAxisType.EdgeB;
                        axis.Index = i;
                        axis.Separation = s;
                        return axis;
                    }

                    // Adjacency
                    if (Vector2.Dot(n, perp) >= 0.0f)
                    {
                        if (Vector2.Dot(n - _upperLimit, _normal) < -Settings.AngularSlop)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (Vector2.Dot(n - _lowerLimit, _normal) < -Settings.AngularSlop)
                        {
                            continue;
                        }
                    }

                    if (s > axis.Separation)
                    {
                        axis.Type = EPAxisType.EdgeB;
                        axis.Index = i;
                        axis.Separation = s;
                    }
                }

                return axis;
            }
        }
    }
}